
namespace mTTS.Services
{
    // https://www.codeproject.com/articles/19071/quick-tool-a-minimalistic-telnet-library

    using System;
    using System.Linq;
    using System.Net.NetworkInformation;
    using System.Net.Sockets;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading.Tasks;
    using mTTS.Utilities;

    enum Verbs
    {
        WILL = 251,
        WONT = 252,
        DO = 253,
        DONT = 254,
        IAC = 255
    }

    enum Options
    {
        SGA = 3
    }

    public class MinimistTelnetClient
    {
        private const int BytesPerLong = 4; // 32 / 8
        private const int BitsPerByte = 8;

        /// <summary>
        /// Sets the keep-alive interval for the socket.
        /// </summary>
        /// <param name="socket">The socket.</param>
        /// <param name="time">Time between two keep alive "pings".</param>
        /// <param name="interval">Time between two keep alive "pings" when first one fails.</param>
        /// <returns>If the keep alive infos were succefully modified.</returns>
        private static async Task<bool> SetKeepAlive( Socket socket, ulong time, ulong interval )
        {
            try
            {
                int retry = 5;
                while ( !socket.Connected )
                {
                    await Task.Delay( 100 );
                    if ( retry-- <= 0 )
                    {
                        SimpleLogger.Log( nameof( MinimistTelnetClient ), "Cannot set KeepAlive Control Value..  Is server up?" );
                        return false;
                    }
                }

                ulong[] input = new[]
                {
                    (time == 0 || interval == 0) ? 0UL : 1UL, // on or off
                    time,
                    interval
                };

                // Pack input into byte struct.
                byte[] inValue = new byte[3 * BytesPerLong];
                for ( int i = 0; i < input.Length; i++ )
                {
                    inValue[i * BytesPerLong + 3] = (byte)(input[i] >> ((BytesPerLong - 1) * BitsPerByte) & 0xff);
                    inValue[i * BytesPerLong + 2] = (byte)(input[i] >> ((BytesPerLong - 2) * BitsPerByte) & 0xff);
                    inValue[i * BytesPerLong + 1] = (byte)(input[i] >> ((BytesPerLong - 3) * BitsPerByte) & 0xff);
                    inValue[i * BytesPerLong + 0] = (byte)(input[i] >> ((BytesPerLong - 4) * BitsPerByte) & 0xff);
                }

                // Create bytestruct for result (bytes pending on server socket).
                byte[] outValue = BitConverter.GetBytes(0);

                // Write SIO_VALS to Socket IOControl.
                // socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.KeepAlive, 10000 );
                socket.IOControl( IOControlCode.KeepAliveValues, inValue, outValue );
            }
            catch ( SocketException e )
            {
                SimpleLogger.Log( nameof( MinimistTelnetClient ), $"Failed to set keep-alive: {e.ErrorCode} {e}" );
                return false;
            }
            SimpleLogger.Log( nameof( MinimistTelnetClient ), "Initialized KeepAlive." );
            return true;
        }

        TcpClient m_tcpSocket;

        public int Timeout { get; set; } = 1000;

        public MinimistTelnetClient( string hostname, int port )
        {
            this.m_tcpSocket = new TcpClient( hostname, port );
            this.m_tcpSocket.Client.SetSocketOption( SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true );
            this.InitiailzeAsync();
        }

        private async void InitiailzeAsync()
        {
            await SetKeepAlive( this.m_tcpSocket.Client, 1200000, 2000 );
        }

        public void Close()
        {
            this.m_tcpSocket.Close();
        }

        public void WriteLine( string cmd )
        {
            this.Write( cmd + "\n" );
        }

        public void Write( string cmd )
        {
            if ( !this.IsConnected ) return;
            byte[] buf = Encoding.ASCII.GetBytes(cmd.Replace("\0xFF","\0xFF\0xFF"));
            this.m_tcpSocket.GetStream().Write( buf, 0, buf.Length );
        }

        public string Read()
        {
            if ( !this.IsConnected ) return null;
            var sb=new StringBuilder();
            do
            {
                this.ParseTelnet( sb );
                System.Threading.Thread.Sleep( this.Timeout );
            } while ( this.m_tcpSocket.Available > 0 );
            return sb.ToString();
        }

        public async Task<string> ReadAsync()
        {
            if ( !this.IsConnected ) return null;
            var sb=new StringBuilder();
            do
            {
                await this.ParseTelnetAsync( sb );
                await Task.Delay( this.Timeout );
            } while ( this.m_tcpSocket.Available > 0 );
            return sb.ToString();
        }

        private async Task ParseTelnetAsync( StringBuilder sb )
        {
            await Task.Run( () => this.ParseTelnet( sb ) );
        }

        public bool IsConnected
        {
            get
            {
                TcpConnectionInformation tcpConn = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
                    .SingleOrDefault(conn =>
                        conn.LocalEndPoint.Equals(this.m_tcpSocket.Client.LocalEndPoint) &&
                        conn.RemoteEndPoint.Equals(this.m_tcpSocket.Client.RemoteEndPoint));
                return tcpConn != null && tcpConn.State == TcpState.Established;
            }
        }

        void ParseTelnet( StringBuilder sb )
        {
            while ( this.m_tcpSocket.Available > 0 )
            {
                int input = this.m_tcpSocket.GetStream().ReadByte();
                switch ( input )
                {
                    case -1:
                        break;
                    case (int)Verbs.IAC:
                        // interpret as command
                        int inputverb = this.m_tcpSocket.GetStream().ReadByte();
                        if ( inputverb == -1 ) break;
                        switch ( inputverb )
                        {
                            case (int)Verbs.IAC:
                                //literal IAC = 255 escaped, so append char 255 to string
                                sb.Append( inputverb );
                                break;
                            case (int)Verbs.DO:
                            case (int)Verbs.DONT:
                            case (int)Verbs.WILL:
                            case (int)Verbs.WONT:
                                // reply to all commands with "WONT", unless it is SGA (suppres go ahead)
                                int inputoption = this.m_tcpSocket.GetStream().ReadByte();
                                if ( inputoption == -1 ) break;
                                this.m_tcpSocket.GetStream().WriteByte( (byte)Verbs.IAC );
                                if ( inputoption == (int)Options.SGA )
                                    this.m_tcpSocket.GetStream().WriteByte( inputverb == (int)Verbs.DO ? (byte)Verbs.WILL : (byte)Verbs.DO );
                                else
                                    this.m_tcpSocket.GetStream().WriteByte( inputverb == (int)Verbs.DO ? (byte)Verbs.WONT : (byte)Verbs.DONT );
                                this.m_tcpSocket.GetStream().WriteByte( (byte)inputoption );
                                break;
                            default:
                                break;
                        }
                        break;
                    default:
                        if ( (char)input == '\r' ) { break; }
                        sb.Append( (char)input );
                        break;
                }
            }
        }
    }
}
