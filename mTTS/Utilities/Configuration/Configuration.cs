
namespace mTTS.Utilities.Configuration
{
    using System;
    using System.IO;
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Json;
    using System.Threading;


    public class Configuration
    {
        private static readonly string ConfigurationFilePath = App.ConfigurationLocation;

        [DataContract]
        private class ConfigurationDataContract
        {
            [DataMember] public string ApiKeyValue;
        }

        private static int _IsInitialized = 0;
        public static bool IsInitialized => _IsInitialized != 0;

        private static ConfigurationDataContract _ConfigurationData = null;

        private static object _ConfigurationLock = new Object();

        public static void Initialize()
        {
            var initializationState = Interlocked.Exchange(ref _IsInitialized, 1);
            if ( initializationState == 1 ) return;

            _ConfigurationData = ReadConfig();

        }

        public static string ApiKey
        {
            get
            {
                Initialize();
                return _ConfigurationData.ApiKeyValue;
            }
            set
            {
                Initialize();
                SaveConfig(value);
            }
        }


        private static ConfigurationDataContract ReadConfig()
        {
            if ( !File.Exists( ConfigurationFilePath ) )
            {
                SaveConfig( "" );
                return _ConfigurationData;
            }

            try
            {
                using ( var reader = File.OpenRead( ConfigurationFilePath ) )
                {
                    var serializer = new DataContractJsonSerializer(typeof(ConfigurationDataContract));
                    lock ( _ConfigurationLock )
                    {
                        if ( _ConfigurationData != null ) { return _ConfigurationData; }
                        _ConfigurationData = (ConfigurationDataContract)serializer.ReadObject( reader );
                    }
                }
            }
            catch ( Exception e)
            {
                SimpleLogger.Log( nameof( Configuration ), e.Message );
                WriteToConfiguration( "Cannot read configuration data at " + ConfigurationFilePath );
            }
            return _ConfigurationData;
        }

        private static void SaveConfig( string apiKey )
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigurationFilePath);
                if ( !Directory.Exists( directory ) )
                {
                    Directory.CreateDirectory( directory );
                }

                using ( var writer = File.OpenWrite( ConfigurationFilePath ) )
                {
                    var serializer = new DataContractJsonSerializer(typeof(ConfigurationDataContract));
                    WriteToConfiguration( apiKey );
                    serializer.WriteObject( writer, _ConfigurationData );
                }
            }
            catch ( Exception e)
            {
                SimpleLogger.Log(nameof(Configuration), e.Message);
                WriteToConfiguration( "Cannot save configuration data at " + ConfigurationFilePath );
            }
        }

        private static void WriteToConfiguration( string apiKey )
        {
            lock ( _ConfigurationLock )
            {
                if ( _ConfigurationData == null )
                {
                    _ConfigurationData = new ConfigurationDataContract();
                }
                _ConfigurationData.ApiKeyValue = apiKey;
            }
        }
    }
}
