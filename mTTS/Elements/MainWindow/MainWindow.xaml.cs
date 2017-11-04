namespace mTTS.Elements.MainWindow
{
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using mTTS.Services;
    using mTTS.Utilities;
    using mTTS.Utilities.Configuration;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            this.InitializeComponent();
            if ( System.ComponentModel.DesignerProperties.GetIsInDesignMode( this ) )
            {
                // looks like we need VM for this to work.
                this.ApiKeyInput.Text = "ABCD-1234-EFGH-7890";
            }
            this.Initialize();
        }

        private bool m_isInitialzed = false;
        private BlockingTextBuffer m_textBuffer;

        private void Initialize()
        {
            if ( this.m_isInitialzed ) return;
            this.m_isInitialzed = true;
            this.m_textBuffer = new BlockingTextBuffer( 200 );
            this.ApiKeyInput.Text = Configuration.ApiKey;
            this.OutputTs3StatusMessage( "Initializing Ts3 Service..." );
            TS3ClientQuery.SetApiKey( Configuration.ApiKey );
            TS3ClientQuery.OnStatusMessageUpdate += this.OutputTs3StatusMessage;
            TS3ClientQuery.StartQueryClientAsync();
            foreach ( var name in SpeechUtil.GetPlaybackDeviceNames() )
            {
                this.PlaybackDevice.Items.Add( name );
                if ( this.m_selectedDevice == null )
                {
                    this.m_selectedDevice = name;
                }
            }
            if ( this.PlaybackDevice.Items.Count != 0 )
            {
                this.PlaybackDevice.SelectedIndex = 0;
            }
            foreach ( var name in SpeechUtil.GetVoiceNames() )
            {
                this.PlaybackVoice.Items.Add( name );
                if ( this.m_selectedVoice == null )
                {
                    this.m_selectedVoice = name;
                }
            }
            if ( this.PlaybackVoice.Items.Count != 0 )
            {
                this.PlaybackVoice.SelectedIndex = 0;
            }
            SpeechUtil.Initialize();

        }

        public string ApiKey { get; set; }

        private async void OutputTs3StatusMessage( string msg )
        {
            await this.m_textBuffer.InsertAsync( msg );
            this.OutputBox.Text = await this.m_textBuffer.GetOutputAsync();
        }


        private void ApiKeyInput_OnLostFocus( object sender, RoutedEventArgs e )
        {
            var apiKey = this.ApiKeyInput.Text;
            if ( TS3ClientQuery.HasConnectionError )
            {
                TS3ClientQuery.ResetConnectionError();
                TS3ClientQuery.SetApiKey( apiKey );
            }

            Configuration.ApiKey = apiKey;
            this.ApiKeyInput.Text = Configuration.ApiKey;
        }

        private string m_oldText = string.Empty;
        private void FunnyText_OnLostFocus( object sender, RoutedEventArgs e )
        {
            if ( this.m_oldText == this.funnyText.Text ) { return; }
            SpeechUtil.TextToSpeech( this.funnyText.Text );
            this.m_oldText = this.funnyText.Text;
        }

        private void ApiKeyInput_OnPreviewKeyDown( object sender, KeyEventArgs e )
        {
            if ( e.Key == Key.Enter && e.IsDown )
            {
                this.ApiKeyInput_OnLostFocus( sender, e );
            }
        }

        private void FunnyText_OnPreviewKeyDown( object sender, KeyEventArgs e )
        {
            if ( e.Key == Key.Enter && e.IsDown )
            {
                this.FunnyText_OnLostFocus( sender, e );
            }
        }

        private string m_selectedDevice;
        private void PlaybackDevice_OnSelectionChanged( object sender, SelectionChangedEventArgs e )
        {
            if ( e.AddedItems == null || e.AddedItems.Count == 0 ) { return; }
            if (e.AddedItems[0] is string s && string.CompareOrdinal(s, this.m_selectedDevice) != 0)
            {
                this.m_selectedDevice = s;
                SpeechUtil.ChangePlaybakcDevice( this.m_selectedDevice );
                SimpleLogger.Log( nameof( MainWindow ), $"Selected Device: {s}" );
            }
            
        }

        private string m_selectedVoice;
        private void PlaybackVoice_OnSelectionChanged( object sender, SelectionChangedEventArgs e )
        {
            if ( e.AddedItems == null || e.AddedItems.Count == 0 ) { return; }
            if ( e.AddedItems[0] is string s && string.CompareOrdinal( s, this.m_selectedVoice ) != 0 )
            {
                this.m_selectedVoice = s;
                SpeechUtil.ChangeVoice( this.m_selectedVoice );
                SimpleLogger.Log( nameof( MainWindow ), $"Selected Voice: {s}" );
            }

        }
    }
}
