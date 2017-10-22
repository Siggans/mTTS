namespace mTTS.Utilities
{
    using System.Threading;
    using System.Windows;

    public class Util
    {
        public static bool IsUiThread
        {
            get
            {
                var uiThread = Application.Current?.Dispatcher?.Thread;
                return uiThread != null && uiThread == Thread.CurrentThread;
            }
        }
    }
}
