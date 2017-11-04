
namespace mTTS.Services
{
    // https://www.codeproject.com/articles/19071/quick-tool-a-minimalistic-telnet-library

    using System;
    using System.IO;
    using System.Linq;
    using System.Net.NetworkInformation;
    using System.Net.Sockets;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading.Tasks;
    using mTTS.Utilities;
    using NAudio.MediaFoundation;

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

        public int Timeout { get; set; } = 1000;
        public Encoding Encoding = Encoding.UTF8;

        public MinimistTelnetClient( string hostname, int port )
        {
            this.m_tcpSocket = new TcpClient( hostname, port );
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
            byte[] buf = this.Encoding.GetBytes(cmd);
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

        private Stream m_utfReader;
        private readonly byte[] m_buffer = new byte[4096];
        void ParseTelnet( StringBuilder sb )
        {
            int count = 0;
            byte b;
            while ( this.m_tcpSocket.Available > 0 )
            {
                this.m_buffer[count++] = b = (byte)this.m_tcpSocket.GetStream().ReadByte();
                if ( (char)b == '\r' ) { continue; }
                if ( count >= this.m_buffer.Length )
                {
                    sb.Append( this.Encoding.GetString( this.m_buffer, 0, count ).Replace( "\r", "" ) );
                    count = 0;
                }
            }
            if ( count != 0 ) { sb.Append( this.Encoding.GetString( this.m_buffer, 0, count ).Replace( "\r", "" ) ); }
        }
    }
}
