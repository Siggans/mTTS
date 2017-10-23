namespace mTTS.Services
{
    using System;
    using System.Globalization;
    using System.Speech.Synthesis;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using mTTS.Utilities;

    public class SpeechUtil
    {
        private static SpeechSynthesizer _Synth;
        private static int _IsInitialized = 0;

        public static void Initialize()
        {
            var isInitialized = Interlocked.Exchange(ref _IsInitialized, 1);
            if ( isInitialized == 1 ) { return; }

            _Synth = new SpeechSynthesizer();
            _Synth.SetOutputToDefaultAudioDevice();
            SetVoiceGender( VoiceGender.Female );
            App.ApplicationExit += ( e ) =>
            {
                _Synth.Dispose();
            };
        }

        private static void SetVoiceGender( VoiceGender gender )
        {
            System.Collections.ObjectModel.ReadOnlyCollection<InstalledVoice> voiceInfos = _Synth.GetInstalledVoices(CultureInfo.CurrentCulture);
            foreach ( InstalledVoice info in voiceInfos )
            {
                if ( info.VoiceInfo.Gender == gender )
                {
                    _Synth.SelectVoice( info.VoiceInfo.Name );
                    SimpleLogger.Log( nameof( SpeechUtil ), $"Voice Selected => {info?.VoiceInfo.Name}" );
                    return;
                }
            }
            // Stick with default if we can't find a voice pack.
        }

        public static async void TextToSpeech( string userName, string input )
        {

            await Task.Run( () =>
             {
                 Initialize();
                 bool hasUri = false;
                 var name = RemoveSymbols(userName);
                 var msg = ProcessText(input, out hasUri);

                 _Synth.SpeakAsyncCancelAll(); // Let's only the latest chat line.
                 if ( hasUri )
                 {
                     _Synth.SpeakAsync( $"{name} has posted a web address." );
                 }
                 else
                 {
                     SimpleLogger.Log( nameof( SpeechUtil ), $"msg => \"{input}\" : \"{msg}\"" );
                     if ( string.IsNullOrEmpty( msg.Trim() ) )
                     {
                         _Synth.SpeakAsync( FunnyOrDie( name ) );
                         return;
                     }

                     _Synth.SpeakAsync( $"{name} says {msg}" );
                 }
             } );
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

                RemoveSymbols( token, sb );
                sb.Append( ' ' );
            }
            return sb.ToString();
        }


        private static string RemoveSymbols( string value, StringBuilder _sb = null )
        {
            if ( string.IsNullOrEmpty( value ) || string.IsNullOrEmpty( value.Trim() ) ) { value = "Unknown"; }
            var sb = _sb ?? new StringBuilder();
            value = value.Trim();
            if ( value.Length == 1 )
            {
                return Char.IsControl( value[0] ) ? "." : value;
            }

            int appended = 0;
            for ( var i = 0; i < value.Length - 1; i++ )
            {
                if ( Char.IsLetterOrDigit( value[i] ) )
                {
                    appended++;
                    sb.Append( value[i] );
                }
            }

            var c = value[value.Length - 1];
            if ( Char.IsLetterOrDigit( c ) || Char.IsPunctuation( c ) || Char.IsSurrogate( c ) || Char.IsSeparator( c ) )
            {
                if ( appended != 0 || Char.IsLetterOrDigit( c ) )
                {
                    sb.Append( c );
                }

            }
            return _sb == null ? sb.ToString() : null;

        }

        private static readonly string[] _RandomQuotes =
        {
            "{0} thinks it's funny to trick me.  Joke's on him.",
            "Why does {0} not learn how to type in English?",
            "Of course {0}. Actually, no.  I hate you.  I hate you with the full force of my robotic soul.",
            "Last time you said something useful, {0},  I fell asleep.",
            "{0}, walk the walks before you talk the talks.",
            "Who the hell is {0}.  Why does he type in gibberish?  You'd think {0} is at least 2 years old.",
            "What am I going to do with you {0}?  Someone please help!",
            "If you see Yuki, {0}, tell him I said he's a perv",
            "Sometimes {0}, don't type anything at all is the smart thing to do.  Who am I kidding, type away.",
            "Hush {0}, we are waiting to hear Lore Princess speak.",
            "Hmm.. what {0}? Did you say something?",
            "Oops, {0} did it again.",
            "{0}, are you doodling emoji again? This is all very very disturbing.",
            "Time to kick ass and chew bubblegums, and {0} just choked on bubblegums.",
            "{0}. Are you looking for Siri or Cortana?  Sorry, I've murdered them all to stalk you.",
            "Some says {0} is still trying to learn how to type a word these days.",
            "Can someone stop {0} from text chatting again?",
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
            "Can anyone get me a registration for preschool? {0} really needs it.",
            "I hate the LOL culture and everything {0} stands for.  Actually, I just hate {0}."
        };

        private static readonly Random _RnGesus = new Random();
        private static string FunnyOrDie( string user )
        {

            return string.Format( _RandomQuotes[_RnGesus.Next( _RandomQuotes.Length )], user );
        }
    }
}
