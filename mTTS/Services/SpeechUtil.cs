namespace mTTS.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Speech.Recognition;
    using System.Speech.Synthesis;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Windows.Controls;
    using System.Windows.Media.TextFormatting;
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
            var voiceInfos = _Synth.GetInstalledVoices(CultureInfo.CurrentCulture);
            // InstalledVoice selectedInfo = null;
            foreach ( var info in voiceInfos )
            {
                // SimpleLogger.Log( nameof( SpeechUtil ), $"Voice List :  {info.VoiceInfo.Name}" );
                if ( info.VoiceInfo.Gender == gender )
                {
                    _Synth.SelectVoice( info.VoiceInfo.Name );
                    SimpleLogger.Log( nameof( SpeechUtil ), $"Voice Selected => {info?.VoiceInfo.Name}" );
                    // selectedInfo = info;
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
                     if ( string.IsNullOrEmpty( msg ) )
                     {
                         msg = "nothing.";
                     }

                     _Synth.SpeakAsync( $"{name} says {msg}" );
                 }
             } );
        }

        private static readonly char[] _Tokenizer = {'\r', '\n', ' ', '\t'};
        private static string ProcessText( string input, out bool hasUri )
        {
            hasUri = false;
            var tokens = input.Split(_Tokenizer, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            foreach ( var token in tokens )
            {
                if ( Uri.IsWellFormedUriString( token, UriKind.Absolute ) )
                {
                    hasUri = true;
                    return input;
                }

                sb.Append( RemoveSymbols( token ) );
                sb.Append( ' ' );
            }
            return sb.ToString();
        }


        private static string RemoveSymbols( string value )
        {
            if ( string.IsNullOrEmpty( value ) || string.IsNullOrEmpty( value.Trim() ) ) { value = "Unknown"; }
            var sb = new StringBuilder();
            value = value.Trim();
            if ( value.Length == 1 )
            {
                return Char.IsControl( value[0] ) ? "." : value;
            }

            for ( var i = 0; i < value.Length - 1; i++ )
            {
                if ( Char.IsLetterOrDigit( value[i] ) )
                {
                    sb.Append( value[i] );
                }
            }
            var c = value[value.Length - 1];
            if ( Char.IsLetterOrDigit( c ) || Char.IsPunctuation( c ) || Char.IsSurrogate( c ) || Char.IsSeparator( c ) )
            {
                sb.Append( c );
            }
            return sb.ToString();

        }
    }
}
