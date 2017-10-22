namespace mTTS.Utilities
{
    using System.Diagnostics;

    public class SimpleLogger
    {
        [Conditional("DEBUG")]
        public static void Log(string message)
        {
            Debug.WriteLine(message);
        }

        [Conditional("DEBUG")]
        public static void Log(string className, string message)
        {
            Log($"{className}: {message}");
        }
    }
}
