
using System;
using System.IO;

namespace ClipboardManager.Utils
{
    public static class FileLogger
    {
        private static readonly string LogFilePath;
        private static readonly object Lock = new object();

        static FileLogger()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClipboardManager");
            Directory.CreateDirectory(appDataPath);
            LogFilePath = Path.Combine(appDataPath, "debug.log");
            Log("Logger initialized.");
        }

        public static void Log(string message)
        {
            try
            {
                lock (Lock)
                {
                    File.AppendAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Don't crash the app if logging fails
            }
        }
    }
}
