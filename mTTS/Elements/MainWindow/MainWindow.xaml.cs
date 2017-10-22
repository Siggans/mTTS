namespace mTTS.Elements.MainWindow
{
    using System.Windows;
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
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
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
            this.m_textBuffer = new BlockingTextBuffer( 1000 );
            this.ApiKeyInput.Text = Configuration.ApiKey;
            TS3ClientQuery.SetApiKey( Configuration.ApiKey);
            TS3ClientQuery.OnStatusMessageUpdate += this.OutputTs3StatusMessage;
            TS3ClientQuery.StartQueryClientAsync();
            SpeechUtil.Initialize();
            this.OutputTs3StatusMessage( "Initializing Ts3 Service..." );
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
                TS3ClientQuery.SetApiKey(apiKey);
            }

            Configuration.ApiKey = apiKey;
            this.ApiKeyInput.Text = Configuration.ApiKey;
        }
    }
}
