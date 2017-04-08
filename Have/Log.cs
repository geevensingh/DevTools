using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;

namespace Have
{
    class Logger
    {
        public enum LevelValue
        {
            Silent,
            Quiet,
            Normal,
            Verbose,
            Warning,
            Error
        };

        static bool _startStopAnnounce = true;
        static List<string> _pending = new List<string>();
        static LevelValue _level = LevelValue.Normal;

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

        internal static LevelValue Level
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

        public static void Start(string description)
        {
            Debug.Assert(!_pending.Contains(description));
            _pending.Add(description);
            if (AnnounceStartStopActions)
            {
                Console.WriteLine(GeneratePrefix() + "Start  " + description);
            }
        }

        public static void Stop(string description)
        {
            Debug.Assert(_pending.Contains(description));
            if (AnnounceStartStopActions)
            {
                Console.WriteLine(GeneratePrefix() + "Stop   " + description);
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
                case LevelValue.Quiet:
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
            ConsoleColor originalColor = Console.ForegroundColor;
            Console.ForegroundColor = GetColorFromLevel(level);
            Console.Write(message);
            Console.ForegroundColor = originalColor;
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
