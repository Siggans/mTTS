
namespace mTTS.Services
{
    // https://www.codeproject.com/articles/19071/quick-tool-a-minimalistic-telnet-library

    using System;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading.Tasks;

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
        TcpClient m_tcpSocket;

        public int m_timeOutMs = 100;

        public MinimistTelnetClient( string Hostname, int Port )
        {
            this.m_tcpSocket = new TcpClient( Hostname, Port );

        }

        public void Close()
        {
            this.m_tcpSocket.Close();
        }

        public string Login( string username, string password, int loginTimeOutMs )
        {
            int oldTimeOutMs = this.m_timeOutMs;
            this.m_timeOutMs = loginTimeOutMs;
            string s = Read();
            if ( !s.TrimEnd().EndsWith( ":" ) )
                throw new Exception( "Failed to connect : no login prompt" );
            WriteLine( username );

            s += Read();
            if ( !s.TrimEnd().EndsWith( ":" ) )
                throw new Exception( "Failed to connect : no password prompt" );
            WriteLine( password );

            s += this.Read();
            this.m_timeOutMs = oldTimeOutMs;
            return s;
        }

        public void WriteLine( string cmd )
        {
            this.Write( cmd + "\n" );
        }

        public void Write( string cmd )
        {
            if ( !this.m_tcpSocket.Connected ) return;
            byte[] buf = Encoding.ASCII.GetBytes(cmd.Replace("\0xFF","\0xFF\0xFF"));
            this.m_tcpSocket.GetStream().Write( buf, 0, buf.Length );
        }

        public string Read()
        {
            if ( !this.m_tcpSocket.Connected ) return null;
            StringBuilder sb=new StringBuilder();
            do
            {
                ParseTelnet( sb );
                System.Threading.Thread.Sleep( this.m_timeOutMs );
            } while ( this.m_tcpSocket.Available > 0 );
            return sb.ToString();
        }

        public async Task<string> ReadAsync()
        {
            if ( !this.m_tcpSocket.Connected ) return null;
            StringBuilder sb=new StringBuilder();
            do
            {
                await this.ParseTelnetAsync( sb );
                await Task.Delay( this.m_timeOutMs );
            } while ( this.m_tcpSocket.Available > 0 );
            return sb.ToString();
        }

        private async Task ParseTelnetAsync( StringBuilder sb )
        {
            await Task.Run( () => this.ParseTelnet( sb ) );
        }

        public bool IsConnected => this.m_tcpSocket.Connected;

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
