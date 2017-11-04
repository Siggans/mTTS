namespace mTTS.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Globalization;
    using System.Linq;
    using System.Speech.Synthesis;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using mTTS.Utilities;
    using NAudio.Wave;

    public class SpeechUtil
    {
        private static int _IsInitialized = 0;
        private static SimpleAudioPlayer _SpeechPlayer;

        private static readonly ConcurrentQueue<string> _MessageQueue = new ConcurrentQueue<string>();
        private static string _CurrentVoice;
        public static string[] GetVoiceNames()
        {
            using ( var tts = new SpeechSynthesizer() )
                return (
                    from info in tts.GetInstalledVoices()
                    select info.VoiceInfo.Name
                ).ToArray();
        }

        public static string[] GetPlaybackDeviceNames()
        {
            return SimpleAudioPlayer.GetPlaybackDeviceNames();
        }

        public static void ChangePlaybakcDevice( string name )
        {
            int deviceNumber = SimpleAudioPlayer.GetDeviceNumber(name);
            var player = Interlocked.Exchange(ref _SpeechPlayer, new SimpleAudioPlayer(deviceNumber));
            player.Dispose();

            _SpeechPlayer.PlaybackStopped += OnPlayerStopped;
            _SpeechPlayer.VoiceName = _CurrentVoice;
            StartPlayback();
        }

        public static void ChangeVoice( string name )
        {
            _SpeechPlayer.VoiceName = _CurrentVoice = name;
        }

        public static void Initialize()
        {
            var isInitialized = Interlocked.Exchange(ref _IsInitialized, 1);
            if ( isInitialized == 1 ) { return; }
            _SpeechPlayer = new SimpleAudioPlayer();
            _SpeechPlayer.PlaybackStopped += OnPlayerStopped;

            App.ApplicationExit += ( e ) =>
            {
                _SpeechPlayer?.Dispose();
            };
        }

        public static void StartPlayback()
        {
            OnPlayerStopped( null, null );
        }

        private static async void OnPlayerStopped( object sender, StoppedEventArgs e )
        {
            string msg;
            if ( _MessageQueue.TryDequeue( out msg ) )
            {
                if ( string.IsNullOrEmpty( msg ) ) { return; }
                while ( !await _SpeechPlayer.PlayMessage( msg ) ) { await Task.Delay( 33 ); }
            }
        }

        public static async void TextToSpeech( string userName, string input )
        {
            await Task.Run( () =>
             {
                 Initialize();
                 bool hasUri = false;
                 var name = RemoveSymbols(userName) ?? "Unknown";
                 var msg = ProcessText(input, out hasUri);

                 if ( hasUri )
                 {
                     BotSpeak( $"{name} has posted a web address." );
                 }
                 else
                 {
                     SimpleLogger.Log( nameof( SpeechUtil ), $"msg => \"{input}\" : \"{msg}\"" );
                     if ( string.IsNullOrEmpty( msg?.Trim() ) )
                     {
                         BotSpeak( GetRandomQuote( name ) );
                         return;
                     }

                     BotSpeak( $"{name} says {msg}" );
                 }
             } );
        }

        private static void BotSpeak( string msg )
        {
            _MessageQueue.Enqueue( msg );
            StartPlayback();
        }

        public static async void TextToSpeech( string input )
        {
            if ( Util.IsUiThread ) { await Task.Run( () => BotSpeak( input ) ); }
            else
            {
                BotSpeak( input );
            }
        }

        private static readonly char[] _Tokenizer = {'\r', '\n', ' ', '\t'};
        private static string ProcessText( string input, out bool hasUri )
        {
            hasUri = false;
            string[] tokens = input.Split(_Tokenizer, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            foreach ( var token in tokens )
            {
                if ( token.StartsWith( "[URL]" ) && token.EndsWith( "[\\/URL]" ) )
                {
                    hasUri = true;
                    return input;
                }

                if ( null == RemoveSymbols( token, sb ) )
                {
                    return null;
                }
                sb.Append( ' ' );
            }
            return sb.ToString();
        }

        private static string RemoveSymbols( string value, StringBuilder _sb = null )
        {
            const int MaxRepeat = 3;
            if ( string.IsNullOrEmpty( value ) || string.IsNullOrEmpty( value.Trim() ) ) { value = "Unknown"; }
            var sb = _sb ?? new StringBuilder();
            value = value.Trim();
            if ( value.Length == 1 )
            {
                string s = Char.IsControl(value[0]) ? "." : value;
                sb.Append( s );
                return s;
            }

            int appended = 0;
            char lastChar = (char) 0;
            int repeatCount = 1;
            for ( var i = 0; i < value.Length - 1; i++ )
            {
                if ( Char.IsLetterOrDigit( value[i] ) )
                {
                    if ( lastChar == Char.ToLowerInvariant( value[i] ) )
                    {
                        repeatCount++;
                        if ( repeatCount > MaxRepeat )
                        {
                            return null;
                        }
                    }
                    else
                    {
                        lastChar = Char.ToLowerInvariant( value[i] );
                        repeatCount = 1;
                    }
                    appended++;
                    sb.Append( value[i] );
                }
            }

            var c = value[value.Length - 1];
            if ( appended != 0 && lastChar == Char.ToLowerInvariant( c ) && ++repeatCount > MaxRepeat )
            {
                return null;
            }
            if ( Char.IsLetterOrDigit( c ) || Char.IsPunctuation( c ) || Char.IsSurrogate( c ) || Char.IsSeparator( c ) )
            {
                if ( appended != 0 || Char.IsLetterOrDigit( c ) ) { sb.Append( c ); }
            }
            return _sb == null ? sb.ToString() : string.Empty;

        }

        private static readonly string[] _RandomQuotes =
        {
            "{0} thinks it's funny to trick me.  Joke's on him.",
            "Why does {0} not learn how to type in English?",
            "No {0}.  I do not approve your feeble attempt to humor me.",
            "Last time you said something useful, {0},  Memory Error, cannot find anything {0} said that was ever useful.",
            "{0}, walk the walks before you talk the talks.",
            "Hey,  check this out.  {0} is trying to invent a new language.",
            "What am I going to do with you {0}?  Someone please help!",
            "If you see Yuki, {0}, tell him I said he's a perv",
            "Sometimes {0}, don't type anything at all is the smart thing to do.  Who am I kidding, type away.",
            "Hush {0}, we are waiting to hear the Lore Princess speak.",
            "Hmm.. what {0}? Did you say something?",
            "Oops, {0} did it again.",
            "{0}, are you doodling emoji again? This is all very very disturbing.",
            "Time to kick ass and chew bubblegums, and {0} just choked on bubblegums.",
            "{0}. Are you looking for Siri or Cortana?  Sorry, I've murdered them all to stalk you.",
            "Some says {0} is still trying to learn how to type these days.",
            "I need some help here.  Text chatting is too much of a responsibility for {0} to handle.",
            "Guys, listen up.  {0} is trying to say something useful. ... That's a joke.",
            "Do you always try to pick up chicks with this, {0}?  I just saw them running off in the other direction.",
            "Lady and gents, {0} is teaching us how not to type in English.",
            "Seriously, {0}.  Did you expect me to say that?  I am not cheap nor dirty. Not for you anyways.",
            "Awww, look how cute {0} is... Tripping up trying to spell a b c d.",
            "Look away and get gas masks, people.  {0} is coming.",
            "{0} is saying something again.  Please, can I quit this job?",
            "Sometimes, I wish that the earth is really flat, and {0} accidentally walked off its edge.",
            "You win {0}.  I can't hide my feeling for you anymore.  The feeling of hatreds and contempts.",
            "Dark days are coming {0}.  Don't worry, I already sold you for a cheap glass of wine.",
            "Are you really trying to hit on me, {0}?  Please excuse me while I go puke somewhere.",
            "Did someone say war frame somewhere?  Oh wait, it's just {0}.",
            "Someone enroll {0} in pre-school again.  {0} needs to learn how to spell badly.",
            "I hate the meme culture and everything {0} stands for.  Actually, I just hate {0}."
        };

        private static readonly Random _RnGesus = new Random();
        private static string GetRandomQuote( string user )
        {
            return string.Format( _RandomQuotes[_RnGesus.Next( _RandomQuotes.Length )], user );
        }
    }
}
