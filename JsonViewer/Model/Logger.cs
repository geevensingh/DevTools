namespace JsonViewer.Model
{
    using System;
    using System.Diagnostics;
    using System.IO;

    internal static class Logger
    {
        public static void Log(string str)
        {
            using (StreamWriter sw = File.AppendText(EnsureLogFilePath()))
            {
                sw.WriteLine(DateTime.Now.ToString("O") + " : " + Process.GetCurrentProcess().Id + " : " + str);
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
