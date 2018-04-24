namespace Json
{
    using System;
    using System.Diagnostics;
    using System.IO;

    internal static class FileLogger
    {
        private static bool _checkedFileTooLarge = false;

        private static string _appName = "JsonViewer";

        public static string AppName { get => _appName; set => _appName = value; }

        public static void Assert(bool condition)
        {
            Assert(condition, string.Empty, skipFrames: 2);
        }

        public static void Assert(bool condition, string message, int skipFrames = 1)
        {
            Debug.Assert(condition);
            if (!condition)
            {
                if (string.IsNullOrEmpty(message))
                {
                    message = "Assert failed!!";
                }

                StackTrace stackTrace = new StackTrace(skipFrames, fNeedFileInfo: true);
                Log(message + "\r\n" + stackTrace.ToString());
            }
        }

        public static void Log(string str)
        {
            Log(new string[] { str });
        }

        public static void Log(string[] lines)
        {
            string preface = DateTime.Now.ToString("O") + " : " + Process.GetCurrentProcess().Id + " : ";
            try
            {
                using (StreamWriter sw = File.AppendText(EnsureLogFilePath()))
                {
                    foreach (string line in lines)
                    {
                        sw.WriteLine(preface + line);
                    }
                }
            }
            catch
            {
            }
        }

        private static string EnsureLogFilePath()
        {
            string logFilePath = GetLogFilePath();

            if (!_checkedFileTooLarge)
            {
                _checkedFileTooLarge = true;

                const uint maxLogSize = 128 * 1024 * 1024;   // 128 MB
                if (File.Exists(logFilePath) && new FileInfo(logFilePath).Length > maxLogSize)
                {
                    TimeSpan age = DateTime.Now - File.GetLastWriteTime(logFilePath);
                    if (age.TotalHours > 2)
                    {
                        try
                        {
                            File.Delete(logFilePath);
                        }
                        catch
                        {
                        }
                    }
                }
            }

            if (!File.Exists(logFilePath))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));
                    using (StreamWriter sw = File.CreateText(logFilePath))
                    {
                        sw.WriteLine("Log file for " + AppName);
                        sw.WriteLine("Started at: " + DateTime.Now.ToString("O"));
                    }

                    File.SetAttributes(logFilePath, FileAttributes.Hidden);
                }
                catch
                {
                }
            }

            return logFilePath;
        }

        private static string GetLogFilePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName + @"\" + AppName + @".log");
        }
    }
}
