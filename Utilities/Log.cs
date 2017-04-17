using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;

namespace Utilities
{
    public class Logger
    {
        public enum LevelValue
        {
            Verbose,
            Normal,
            Warning,
            Error,
            Silent,
        };

        static string _logFile = string.Empty;
        static bool _startStopAnnounce = true;
        static List<string> _pending = new List<string>();
        static LevelValue _level = LevelValue.Normal;

        public static string LogFile
        {
            get
            {
                return _logFile;
            }
            set
            {
                _logFile = value;
                if (File.Exists(_logFile))
                {
                    File.Delete(_logFile);
                }
            }
        }

        public static bool AnnounceStartStopActions
        {
            get
            {
                return _startStopAnnounce;
            }

            set
            {
                _startStopAnnounce = value;
            }
        }

        public static LevelValue Level
        {
            get
            {
                return _level;
            }

            set
            {
                _level = value;
            }
        }

        public static void Start(string description, LevelValue level = LevelValue.Verbose)
        {
            Debug.Assert(!_pending.Contains(description));
            _pending.Add(description);
            if (AnnounceStartStopActions)
            {
                LogLine(GeneratePrefix() + "Start  " + description, level);
            }
        }

        public static void Stop(string description, LevelValue level = LevelValue.Verbose)
        {
            Debug.Assert(_pending.Contains(description));
            if (AnnounceStartStopActions)
            {
                LogLine(GeneratePrefix() + "Stop   " + description, level);
            }
            _pending.Remove(description);
        }

        static string GeneratePrefix()
        {
            string prefix = "\t" + DateTime.Now.ToLongTimeString() + " : ";
            for (int ii = 0; ii < _pending.Count; ii++)
            {
                prefix += "\t";
            }
            return prefix;
        }

        static ConsoleColor GetColorFromLevel(LevelValue level)
        {
            switch (level)
            {
                case LevelValue.Silent:
                case LevelValue.Normal:
                    return ConsoleColor.White;
                case LevelValue.Verbose:
                    return ConsoleColor.Gray;
                case LevelValue.Warning:
                    return ConsoleColor.Yellow;
                case LevelValue.Error:
                    return ConsoleColor.Red;
                default:
                    Debug.Assert(false);
                    return ConsoleColor.Magenta;
            }
        }

        public static void Log(string message)
        {
            Log(message, LevelValue.Normal);
        }

        public static void Log(string message, LevelValue level)
        {
            if (level >= Level)
            {
                Console.ForegroundColor = GetColorFromLevel(level);
                Console.Write(message);
                Console.ResetColor();
            }
            if (!string.IsNullOrEmpty(_logFile))
            {
                File.AppendAllText(_logFile, message);
            }
        }

        public static void LogLine(string message)
        {
            Log(message + "\r\n");
        }

        public static void LogLine(string message, LevelValue level)
        {
            Log(message + "\r\n", level);
        }
    }
}
