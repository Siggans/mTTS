namespace mTTS.Services
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Speech.AudioFormat;
    using System.Speech.Synthesis;
    using System.Threading.Tasks;
    using mTTS.Utilities;
    using NAudio.Wave;

    public class SimpleAudioPlayer : IDisposable
    {
        [Conditional( "DEBUG" )]
        private static void Log( string message )
        {
            SimpleLogger.Log( nameof( SimpleAudioPlayer ), message );
        }

        public static string[] GetPlaybackDeviceNames()
        {
            int count = WaveOut.DeviceCount;
            string[] names = new string[count];
            for ( var deviceNumber = 0; deviceNumber < count; deviceNumber++ )
            {
                names[deviceNumber] = WaveOut.GetCapabilities( deviceNumber ).ProductName;
            }
            return names;
        }

        public static int GetDeviceNumber( string name )
        {
            int count = WaveOut.DeviceCount;
            for ( var deviceNumber = 0; deviceNumber < count; deviceNumber++ )
            {
                if ( string.CompareOrdinal( name, WaveOut.GetCapabilities( deviceNumber ).ProductName ) != 0 ) continue;

                Log( $"Selected device {WaveOut.GetCapabilities( deviceNumber ).ProductName}" );
                return deviceNumber;
            }
            return 0;
        }

        public EventHandler<StoppedEventArgs> PlaybackStopped;

        private readonly WaveOut m_waveOut;
        private readonly MixingWaveProvider32 m_mixer;
        public string VoiceName { get; set; }

        public bool IsPlaying => this.m_waveOut.PlaybackState == PlaybackState.Playing;

        public SimpleAudioPlayer( int deviceNumber = 0 )
        {
            this.m_waveOut = new WaveOut
            {
                DeviceNumber = deviceNumber
            };
            this.m_mixer = new MixingWaveProvider32();
            this.m_waveOut.Init( this.m_mixer );
            this.m_waveOut.PlaybackStopped += this.OnPlaybackStopped;
        }

        private readonly object m_streamProviderLock = new Object();
        private MemoryStream m_stream;
        private IWaveProvider m_provider;
        private async void OnPlaybackStopped( object sender, StoppedEventArgs e )
        {
            if ( Util.IsUiThread ) { await Task.Run( () => this.OnPlaybackStopped( sender, e ) ); }
            else
            {
                lock ( this.m_streamProviderLock )
                {
                    if ( this.m_stream != null )
                    {
                        this.m_stream.Close();
                        this.m_stream = null;
                        this.m_mixer.RemoveInputStream( this.m_provider );
                        this.m_provider = null;
                    }
                }
                this.PlaybackStopped?.Invoke( sender, e );
            }
        }

        public async Task<bool> PlayMessage( string message )
        {
            if ( Util.IsUiThread ) { return await Task.Run( () => this.PlayMessage( message ) ); }

            using ( var tts = new SpeechSynthesizer() )
            {
                lock ( this.m_streamProviderLock )
                {
                    try
                    {
                        if ( this.m_stream != null ) { return false; }
                        this.m_stream = new MemoryStream( 1024 * 1024 );

                        var name = this.VoiceName;
                        if ( !string.IsNullOrEmpty( name ) )
                        {
                            tts.SelectVoice( name );
                        }
                        tts.SetOutputToAudioStream( this.m_stream,
                            new SpeechAudioFormatInfo( 44100, AudioBitsPerSample.Sixteen, AudioChannel.Stereo ) );
                        tts.Speak( message );
                        this.m_stream.Flush();
                        this.m_stream.Seek( 0, SeekOrigin.Begin );
                        this.m_provider = new Wave16ToFloatProvider(
                            new RawSourceWaveStream( this.m_stream, new WaveFormat( 44100, 16, 2 ) ) );
                        this.m_mixer.AddInputStream( this.m_provider );
                        this.m_waveOut.Play();
                    }
                    catch ( Exception e )
                    {
                        this.m_stream?.Close();
                        this.m_stream = null;
                        if ( this.m_provider != null )
                        {
                            this.m_mixer.RemoveInputStream( this.m_provider );
                            this.m_provider = null;
                        }
                        return false;
                    }
                }
            }

            return true;
        }

        #region Dispose pattern
        private bool m_disposed = false;
        protected void Dispose( bool disposing )
        {
            if ( this.m_disposed ) { return; }

            if ( disposing )
            {

                if ( this.m_waveOut != null )
                {
                    this.m_waveOut.PlaybackStopped -= this.PlaybackStopped;
                    this.m_waveOut.Dispose();
                }
                lock ( this.m_streamProviderLock ) this.m_stream?.Dispose();
                if ( this.PlaybackStopped != null )
                {
                    foreach ( EventHandler<StoppedEventArgs> handler in this.PlaybackStopped.GetInvocationList() )
                    {
                        this.PlaybackStopped -= handler;
                    }
                }

            }
            this.m_disposed = true;
        }

        public void Dispose()
        {
            this.Dispose( true );
            GC.SuppressFinalize( this );
        }

        #endregion Dispose pattern
    }
}
