namespace mTTS
{
    using mTTS.Services;
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Data;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Windows;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static readonly string ConfigurationLocation = Environment.ExpandEnvironmentVariables("%LocalAppData%\\Siggans\\mTT\\config.json");
        public static event ApplicationExitHandler ApplicationExit;

        protected override void OnExit(ExitEventArgs e)
        {
            ApplicationExit?.Invoke( e );
        }
    }

    public delegate void ApplicationExitHandler(ExitEventArgs e);
}
