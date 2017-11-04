namespace mTTS.Services
{
    using System;
    using System.Diagnostics;
    using System.Net;
    using System.Net.Sockets;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;
    using mTTS.Utilities;

    public enum CQConnectionStatus
    {
        Disconnected,
        Connected,
        AuthKeyError,
        ConnectionReset,
        WaitingForServer,
        CannotRecover,
        Paused
    }

    public delegate void CQStatusMessageEventHandler( string s );

    public class TS3ClientQuery
    {
        #region Static

        const int RetryTimerInSeconds = 5;
        public static event CQStatusMessageEventHandler OnStatusMessageUpdate;

        public static CQConnectionStatus ConnectionStatus { get; private set; } = CQConnectionStatus.Disconnected;
        private static readonly SemaphoreSlim Semaphore = new SemaphoreSlim(2);
        public static Exception LastException { get; private set; } = null;

        public static bool HasConnectionError => ConnectionStatus == CQConnectionStatus.AuthKeyError || ConnectionStatus == CQConnectionStatus.CannotRecover;

        private static string _ApiKey = "";

        public static void SetApiKey( string key )
        {
            _ApiKey = key;
        }

        public static void ResetConnectionError()
        {
            if ( HasConnectionError )
            {
                ConnectionStatus = CQConnectionStatus.Disconnected;
            }
        }

        public static void PauseConnection()
        {
            ConnectionStatus = CQConnectionStatus.Paused;
        }

        public static async void StartQueryClientAsync()
        {
            // Allow only one call to pass
            await TS3ClientQuery.Semaphore.WaitAsync();

            Debug.WriteLine( $"Current Semaphore Value is {TS3ClientQuery.Semaphore.CurrentCount}." );
            if ( TS3ClientQuery.Semaphore.CurrentCount == 0 )
            {
                TS3ClientQuery.Semaphore.Release();
                return;
            }

            ConnectionStatus = CQConnectionStatus.WaitingForServer;
            try
            {
                while ( true )
                {
                    try
                    {
                        if ( !HasConnectionError && ConnectionStatus != CQConnectionStatus.Paused )
                        {
                            var client = new TS3ClientQuery("127.0.0.1", 25639, _ApiKey);
                            CQConnectionStatus status = await client.ConnectAsync();
                            var message = GetConnectionStatusDescription(status);
                            SimpleLogger.Log( nameof( TS3ClientQuery ), message );
                            if ( ConnectionStatus != CQConnectionStatus.Paused )
                            {
                                ConnectionStatus = status;
                            }

                            if ( ConnectionStatus == CQConnectionStatus.ConnectionReset
                                || ConnectionStatus == CQConnectionStatus.Disconnected
                                || ConnectionStatus == CQConnectionStatus.WaitingForServer )
                            {
                                message += $"\nRetrying connection in {RetryTimerInSeconds} seconds...";
                            }
                            SendStatueMessage( message );
                        }
                        if ( ConnectionStatus == CQConnectionStatus.Paused ) { break; }
                        await Task.Delay( RetryTimerInSeconds * 1000 );
                    }
                    catch ( Exception ) { }
                }
            }
            finally
            {

                SendStatueMessage( "Auto connection to TS Client is now paused" );
                TS3ClientQuery.Semaphore.Release();
            }
        }

        private static string GetConnectionStatusDescription( CQConnectionStatus status )
        {

            switch ( status )
            {
                case CQConnectionStatus.AuthKeyError:
                    return "Cannot authenticate with query server.\n** Ensure the API Key is correct **";
                case CQConnectionStatus.Connected:
                    return "Connected...";
                case CQConnectionStatus.Disconnected:
                    return "Disconnected...";
                case CQConnectionStatus.ConnectionReset:
                    return "Connection was interrupted...";
                case CQConnectionStatus.WaitingForServer:
                    return "Connection refused.  Is TS3 and ClientQuery Addon enabled?";
                case CQConnectionStatus.CannotRecover:
                    return
                        "Connection was established but protocol cannot be determined.\nmTT Cannot Recover from this error!";
                case CQConnectionStatus.Paused:
                    return "Connection to TS3 Client is being stopped";
                default:
                    return $"Unknown status {(Enum.GetName( typeof( CQConnectionStatus ), status ))}";
            }
        }

        private static void SendStatueMessage( string message )
        {
            CQStatusMessageEventHandler eventHandlers = OnStatusMessageUpdate;
            eventHandlers?.Invoke( message );
        }

        #endregion Static

        private readonly IPEndPoint m_ip;
        private readonly string m_ts3ApiKey;
        private MinimistTelnetClient m_client = null;
        public int KeepAliveRetryTimer = 90 * 1000;

        private TS3ClientQuery( string ip, UInt16 port, string apiKey )
        {
            if ( !IPAddress.TryParse( ip, out var ipAddress ) )
            {
                throw new ArgumentException( "ip: Cannot be convert to valid IP address." );
            }
            if ( port == 0 )
            {
                throw new ArgumentException( "port: Value cannot be zero." );
            }
            this.m_ip = new IPEndPoint( ipAddress, port );
            this.m_ts3ApiKey = apiKey;
        }

        public async Task<CQConnectionStatus> ConnectAsync()
        {
            try
            {
                this.m_client = new MinimistTelnetClient(this.m_ip.Address.ToString(), this.m_ip.Port)
                {
                    Timeout = 1000 / 30
                };
                App.ApplicationExit += this.ApplicationExitHandler;

                Log( "Starting query" );
                return await this.CaptureChatMessages();
            }
            catch ( Exception e )
            {
                MinimistTelnetClient client = this.m_client;
                TS3ClientQuery.LastException = e;
                client?.Close();

                Log( "Exception Occurred: " + e.Message );
                if ( e is SocketException ex && ex.ErrorCode == 10061 )
                {
                    return CQConnectionStatus.WaitingForServer;
                }
                return CQConnectionStatus.ConnectionReset;
            }
            finally
            {
                this.ApplicationExitHandler( null );
                App.ApplicationExit -= this.ApplicationExitHandler;
            }
        }

        public async Task<CQConnectionStatus> CaptureChatMessages()
        {
            // Consume Headers.
            if ( !await this.ConsumeHeadersAsync() ) { return CQConnectionStatus.CannotRecover; }

            // Authentication Process.
            Log( $"Attempting to Authorize with \"auth apikey={this.m_ts3ApiKey}\"..." );

            this.m_client.WriteLine( $"auth apikey={this.m_ts3ApiKey}" );

            var response = new ParseMessage(await this.m_client.ReadAsync());
            if ( !response.IsValid || response.IsError )
            {
                return response.IsValid ? CQConnectionStatus.AuthKeyError : CQConnectionStatus.WaitingForServer;
            }

            // Start hooking up events for TTS feature.
            Log( "Connection established" );
            SendStatueMessage( "Connection Established!" );

            if ( !await this.RegisterForTextMessagesAsync() ) { return CQConnectionStatus.CannotRecover; }

            this.TeamSpeakCQKeepAlive( initialize: true ); // Start the timing mechanism.
            while ( true )
            {
                string textMsg = await this.m_client.ReadAsync(); // Automatically returns after client's built in timeout.
                if ( textMsg == null ) { return CQConnectionStatus.Disconnected; }
                if ( textMsg == "" )
                {
                    this.TeamSpeakCQKeepAlive(); // Let's do the keep-alive event here.
                    if ( ConnectionStatus == CQConnectionStatus.Paused )
                    {
                        this.m_client.WriteLine( "quit" );
                    }
                    continue;
                }
                if ( textMsg.EndsWith( "\n" ) ) { textMsg = textMsg.Substring( 0, textMsg.Length - 1 ); }
                LogClientResponse( textMsg );
                this.PrepareTtsSendOff( textMsg );
            }
        }

        private int m_startTimeKA;
        private void TeamSpeakCQKeepAlive( bool initialize = false )
        {
            // ClientQuery ignores KeepAlive request and seems to cut off the connection every 10-20 minutes. We will
            // try to send a 'use' command every few minutes to keep the connection going. The return response is 
            // unimportant to us as the message loop will eat up and discard the 'use' response, and we won't need to worry 
            // about accidentally eating up the notification response.
            if ( initialize )
            {
                this.m_startTimeKA = Environment.TickCount;
                return;
            }

            var duration = Environment.TickCount - this.m_startTimeKA;
            if ( duration >= this.KeepAliveRetryTimer )
            {
                this.m_startTimeKA = Environment.TickCount;
                this.m_client.WriteLine( "" ); // Do an empty cmd for keep-alive.
            }
        }

        private static readonly Regex _TextMessageEventMatcher =
            new Regex(
                @"^notifytextmessage schandlerid=\S+ targetmode=\S+ msg=(\S+) invokerid=\S+ invokername=(\S+) invokeruid=\S+$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private void PrepareTtsSendOff( string textMsg )
        {
            // We may receive a multi-line response here.  Let's process them all.
            string[] tokens = textMsg.Split(new [] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
            foreach ( var token in tokens )
            {
                Match match = _TextMessageEventMatcher.Match(token);
                if ( !match.Success )
                {
                    return;
                }

                var user = match.Groups[2].Value.Replace("\\s", " ");
                var message = match.Groups[1].Value.Replace("\\s", " ");
                SendStatueMessage( $"User {user} entered \"{message}\"" );
                SpeechUtil.TextToSpeech( user, message );
            }

        }

        private async Task<bool> RegisterForTextMessagesAsync()
        {
            string id = await this.GetHandlerIdAsync();

            if ( id == null ) { return false; }

            return await this.RegisterEventAsync( id, "notifytextmessage" );
        }

        private async Task<bool> RegisterEventAsync( string id, string eventName )
        {
            var response = new ParseMessage(
                await this.SendCommandAsync($"clientnotifyregister schandlerid={id} event={eventName}"));
            return response.IsValid && !response.IsError;
        }

        private static readonly Regex _UseCmdMatch = new Regex(@"^selected schandlerid=(\S)+\n", RegexOptions.Compiled | RegexOptions.Multiline);
        private async Task<string> GetHandlerIdAsync()
        {
            string response = await this.SendCommandAsync("use");
            Match match = _UseCmdMatch.Match(response);
            if ( !match.Success )
            {
                Log( "Error matching response data ..." );
                return null;
            }
            return match.Groups[1].Value;
        }

        private async Task<string> SendCommandAsync( string cmd )
        {
            LogClientSend( cmd );
            this.m_client.WriteLine( cmd );
            string response = await this.m_client.ReadAsync();
            LogClientResponse( response );
            return response;
        }

        private async Task<bool> ConsumeHeadersAsync()
        {
            string msg = await this.m_client.ReadAsync();

            SimpleLogger.Log( nameof( TS3ClientQuery ), $"Received Header: {msg}" );

            return msg != null;
        }

        private void ApplicationExitHandler( ExitEventArgs e )
        {
            MinimistTelnetClient client = Interlocked.Exchange(ref this.m_client, null);
            if ( client != null && client.IsConnected ) { client.Close(); }
        }

        [Conditional( "DEBUG" )]
        private static void LogClientResponse( string msg )
        {
            Log( $" <= {msg}" );
        }

        [Conditional( "DEBUG" )]
        private static void LogClientSend( string msg )
        {
            Log( $" => {msg}" );
        }

        [Conditional( "DEBUG" )]
        private static void Log( string msg )
        {
            if ( msg != null && msg.EndsWith( "\n" ) ) { msg = msg.Substring( 0, msg.Length - 1 ); }
            SimpleLogger.Log( nameof( TS3ClientQuery ), msg ?? "" );
        }

        private class ParseMessage
        {
            static readonly Regex ErrorMessageRegex = new Regex(@"error\s+id=(\S+)\s+msg=(\S+)\s*$", RegexOptions.Compiled );
            public string Input { get; private set; }

            public bool IsValid { get; private set; } = false;

            public string ErrorCode { get; private set; } = "UNKNOWN";
            public string Message { get; private set; } = "NO MESSAGE";

            public bool IsError => string.CompareOrdinal( "0", this.ErrorCode ) != 0;

            public ParseMessage( string input )
            {
                if ( input != null && input.EndsWith( "\n" ) ) { input = input.Substring( 0, input.Length - 1 ); }
                this.Input = input;
                SimpleLogger.Log( nameof( ParseMessage ), $"Api Response Input was \"{input}\"" );
                this.ParseInput();
                SimpleLogger.Log( nameof( ParseMessage ), $"Result message parsing has {(this.IsValid && !this.IsError ? "Successful" : "Failed")}" );
            }

            private void ParseInput()
            {
                Match match = ParseMessage.ErrorMessageRegex.Match(this.Input);
                if ( match.Success )
                {
                    this.IsValid = true;
                    this.ErrorCode = match.Groups[1].Value;
                    this.Message = match.Groups[2].Value?.Replace( "\\s", " " );
                }
            }
        }
    }
}
