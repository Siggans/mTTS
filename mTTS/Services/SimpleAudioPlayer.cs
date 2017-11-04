namespace mTTS.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Speech.AudioFormat;
    using System.Speech.Synthesis;
    using System.Threading;
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
        private readonly Mixer32 m_mixer;
        public string VoiceName { get; set; }

        public bool IsPlaying => this.m_waveOut.PlaybackState == PlaybackState.Playing;

        public SimpleAudioPlayer( int deviceNumber = 0 )
        {
            this.m_waveOut = new WaveOut
            {
                DeviceNumber = deviceNumber
            };
            this.m_mixer = new Mixer32(true);
            this.m_waveOut.Init( this.m_mixer );
            this.m_waveOut.PlaybackStopped += this.OnPlaybackStopped;
        }

        private async void OnPlaybackStopped( object sender, StoppedEventArgs e )
        {
            if ( Util.IsUiThread ) { await Task.Run( () => this.OnPlaybackStopped( sender, e ) ); }
            else { this.PlaybackStopped?.Invoke( sender, e ); }
        }

        private const int OneMeg = 1024 * 1024;
        private readonly SemaphoreSlim m_playbackSemaphore = new SemaphoreSlim(1);
        public async Task<bool> PlayMessage( string message )
        {
            if ( Util.IsUiThread ) { return await Task.Run( () => this.PlayMessage( message ) ); }

            await this.m_playbackSemaphore.WaitAsync();

            Stream mStream = null;
            try
            {
                if (this.IsPlaying) { return false; }

                mStream = new MemoryStream( OneMeg );

                var name = this.VoiceName;
                using (var tts = new SpeechSynthesizer())
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        tts.SelectVoice(name);
                    }
                    tts.SetOutputToAudioStream(mStream,
                        new SpeechAudioFormatInfo(44100, AudioBitsPerSample.Sixteen, AudioChannel.Stereo));
                    tts.Speak(message);
                }
                mStream.Flush();
                mStream.Seek(0, SeekOrigin.Begin);
                this.m_mixer.AddInputStream(new DisposingWaveProvider(mStream));
                this.m_waveOut.Play();
            }
            catch (Exception e)
            {
                SimpleLogger.Log(nameof(SimpleAudioPlayer), $"Exception {e.GetType().Name} has occurred: {e.Message}");
                mStream?.Dispose();
                return false;
            }
            finally
            {
                this.m_playbackSemaphore.Release();
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
                this.PlaybackStopped = null;
            }
            this.m_disposed = true;
        }

        public void Dispose()
        {
            this.Dispose( true );
            GC.SuppressFinalize( this );
        }

        #endregion Dispose pattern

        #region Extending WaveProvider32

        // We are implementing our own mixer so that if an input has exhausted its input, the input will be dropped
        // and disposed for us, provided that the input is IDisposable.
        public class Mixer32 : IWaveProvider
        {
            public bool DisposeInputOnCompletion { get; set; } = false;

            private readonly LinkedList<IWaveProvider> m_inputs = new LinkedList<IWaveProvider>();
            private readonly int m_bytesPerSample;
            private WaveFormat m_waveFormat;

            /// <summary>Creates a new MixingWaveProvider32</summary>
            public Mixer32()
            {
                this.m_waveFormat = WaveFormat.CreateIeeeFloatWaveFormat( 44100, 2 );
                this.m_bytesPerSample = 4;
            }

            public Mixer32( bool shouldDisposeInputOnCompletion ) : this()
            {
                this.DisposeInputOnCompletion = shouldDisposeInputOnCompletion;
            }

            /// <summary>Add a new input to the mixer</summary>
            /// <param name="waveProvider">The wave input to add</param>
            public void AddInputStream( IWaveProvider waveProvider )
            {
                if ( waveProvider.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat )
                {
                    throw new ArgumentException( "Must be IEEE floating point", "waveProvider.WaveFormat" );
                }
                if ( waveProvider.WaveFormat.BitsPerSample != 32 )
                {
                    throw new ArgumentException( "Only 32 bit audio currently supported", "waveProvider.WaveFormat" );
                }
                if ( this.m_inputs.Count == 0 )
                {
                    this.m_waveFormat = WaveFormat.CreateIeeeFloatWaveFormat( waveProvider.WaveFormat.SampleRate,
                        waveProvider.WaveFormat.Channels );
                }
                else if ( !waveProvider.WaveFormat.Equals( (object)this.m_waveFormat ) )
                {
                    throw new ArgumentException( "All incoming channels must have the same format",
                        "waveProvider.WaveFormat" );
                }
                lock ( this.m_inputs )
                {
                    this.m_inputs.AddLast( waveProvider );
                }
            }

            /// <summary>Remove an input from the mixer</summary>
            /// <param name="waveProvider">waveProvider to remove</param>
            public void RemoveInputStream( IWaveProvider waveProvider )
            {
                lock ( this.m_inputs )
                {
                    this.m_inputs.Remove( waveProvider );
                }
            }

            /// <summary>The number of inputs to this mixer</summary>
            public int InputCount => this.m_inputs.Count;

            /// <summary>Reads bytes from this wave stream</summary>
            /// <param name="buffer">buffer to read into</param>
            /// <param name="offset">offset into buffer</param>
            /// <param name="count">number of bytes required</param>
            /// <returns>Number of bytes read.</returns>
            /// <exception cref="T:System.ArgumentException">Thrown if an invalid number of bytes requested</exception>
            public int Read( byte[] buffer, int offset, int count )
            {
                if ( count % this.m_bytesPerSample != 0 )
                {
                    throw new ArgumentException( "Must read an whole number of samples", nameof( count ) );
                }
                Array.Clear( (Array)buffer, offset, count );
                int val1 = 0;
                byte[] numArray = new byte[count];
                lock ( this.m_inputs )
                {
                    var node = this.m_inputs.First;
                    while ( node != null )
                    {
                        int num = node.Value.Read(numArray, 0, count);
                        val1 = Math.Max( val1, num );
                        if ( num > 0 )
                            Mixer32.Sum32BitAudio( buffer, offset, numArray, num );
                        else
                        {
                            // Input exhaustion.  We should remove and dispose if we can.
                            this.m_inputs.Remove( node );
                            if ( this.DisposeInputOnCompletion && node.Value is IDisposable x )
                            {
                                x.Dispose();
                            }
                        }
                        node = node.Next;
                    }
                }
                return val1;
            }

            /// <summary>Actually performs the mixing</summary>
            private static unsafe void Sum32BitAudio( byte[] destBuffer, int offset, byte[] sourceBuffer, int bytesRead )
            {
                fixed ( byte* numPtr1 = &destBuffer[offset] )
                fixed ( byte* numPtr2 = &sourceBuffer[0] )
                {
                    float* numPtr3 = (float*) numPtr1;
                    float* numPtr4 = (float*) numPtr2;
                    int num1 = bytesRead / 4;
                    for ( int index = 0; index < num1; ++index )
                    {
                        IntPtr num2 = (IntPtr) (numPtr3 + index);
                        double num3 = (double) *(float*) num2 + (double) numPtr4[index];
                        *(float*)num2 = (float)num3;
                    }
                }
            }

            /// <summary>
            /// <see cref="P:NAudio.Wave.WaveStream.WaveFormat" />
            /// </summary>
            public WaveFormat WaveFormat => this.m_waveFormat;
        }

        public class DisposingWaveProvider : Wave16ToFloatProvider, IDisposable
        {
            private readonly Stream m_streamSource;

            public DisposingWaveProvider( Stream streamSource ) : base( new RawSourceWaveStream( streamSource, new WaveFormat( 44100, 16, 2 ) ) )
            {
                this.m_streamSource = streamSource;
            }

            #region Dispose pattern
            private bool m_disposed = false;
            protected void Dispose( bool disposing )
            {
                if ( this.m_disposed ) { return; }
                if ( disposing ) { this.m_streamSource?.Close(); }
                this.m_disposed = true;
            }

            public void Dispose()
            {
                this.Dispose( true );
                GC.SuppressFinalize( this );
            }

            #endregion Dispose pattern
        }
        #endregion Extending WaveProvider32
    }
}
