namespace JsonViewer.Model
{
    using System;
    using System.Diagnostics;
    using System.IO;

    internal static class FileLogger
    {
        public static void Assert(bool condition)
        {
            Assert(condition, string.Empty);
        }

        public static void Assert(bool condition, string message)
        {
            Debug.Assert(condition);
            if (!condition)
            {
                Log(new string[] { string.IsNullOrEmpty(message) ? "Assert failed!!" : message, Environment.StackTrace });
            }
        }

        public static void Log(string str)
        {
            Log(new string[] { str });
        }

        public static void Log(string[] lines)
        {
            string preface = DateTime.Now.ToString("O") + " : " + Process.GetCurrentProcess().Id + " : ";
            using (StreamWriter sw = File.AppendText(EnsureLogFilePath()))
            {
                foreach (string line in lines)
                {
                    sw.WriteLine(preface + line);
                }
            }
        }

        private static string EnsureLogFilePath()
        {
            string logFilePath = GetLogFilePath();
            if (!File.Exists(logFilePath))
            {
                using (StreamWriter sw = File.CreateText(logFilePath))
                {
                    sw.WriteLine("Log file for JsonViewer");
                    sw.WriteLine("Started at: " + DateTime.Now.ToString("O"));
                }
            }

            return logFilePath;
        }

        private static string GetLogFilePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"JsonViewer\JsonViewer.log");
        }
    }
}
