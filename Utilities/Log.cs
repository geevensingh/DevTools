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
            SuperChatty,
            Verbose,
            Normal,
            Warning,
            Error,
            Silent,
        };

        private static LevelValue _defaultLevel = LevelValue.Normal;

        static LevelValue[] _allLevels = new LevelValue[] { LevelValue.SuperChatty, LevelValue.Verbose, LevelValue.Normal, LevelValue.Warning, LevelValue.Error, LevelValue.Silent };

        static Dictionary<LevelValue, string> _logFiles = new Dictionary<LevelValue, string>();
        static Dictionary<LevelValue, string> _htmlFiles = new Dictionary<LevelValue, string>();
        static bool _startStopAnnounce = false;
        static List<string> _pending = new List<string>();
        static LevelValue _level = _defaultLevel;
        static uint _warningCount = 0;
        static uint _errorCount = 0;

        public static void AddLogFile(string v)
        {
            AddLogFile(v, _defaultLevel);
        }

        public static void AddLogFile(string v, LevelValue level)
        {
            Debug.Assert(!_logFiles.ContainsKey(level));
            _logFiles.Add(level, v);
            if (File.Exists(v))
            {
                File.Delete(v);
            }
        }

        public static void AddHTMLLogFile(string v)
        {
            AddHTMLLogFile(v, _defaultLevel);
        }

        public static void AddHTMLLogFile(string v, LevelValue level)
        {
            Debug.Assert(!_htmlFiles.ContainsKey(level));
            _htmlFiles.Add(level, v);
            File.WriteAllText(v, @"<html><head><style>
html { white-space: nowrap; font-family: Verdana; background-color: #e0e0e0; }
.verbose { color: gray; }
.normal { color: black; }
.warning { color: darkred; }
.error { color: red; }
</style></head>
<body>");
        }

        public static void FlushLogs()
        {
            foreach (string htmlLog in _htmlFiles.Values)
            {
                File.AppendAllText(htmlLog, @"</body></html>");
            }
        }

        public static string VerboseLogPath
        {
            get
            {
                if (_logFiles.ContainsKey(LevelValue.Verbose))
                {
                    return _logFiles[LevelValue.Verbose];
                }
                return string.Empty;
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

        public static uint WarningCount
        {
            get
            {
                return _warningCount + ErrorCount;
            }
        }

        public static uint ErrorCount { get => _errorCount; }

        public static void Start(string description, LevelValue level = LevelValue.Verbose)
        {
            Debug.Assert(!_pending.Contains(description));
            _pending.Add(description);
            if (AnnounceStartStopActions)
            {
                LogLine(GeneratePrefix() + "Start  " + description, level);
            }
        }

        public static void Stop(string description, LevelValue level = LevelValue.Verbose, string[] output = null)
        {
            Debug.Assert(_pending.Contains(description));
            if (AnnounceStartStopActions)
            {
                if (output != null)
                {
                    foreach(string line in output)
                    {
                        LogLine(GeneratePrefix() + "       " + line, level);
                    }
                }
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
        static LevelValue[] GetLevels(LevelValue level)
        {
            List<LevelValue> levels = new List<LevelValue>();
            foreach (LevelValue testLevel in _allLevels)
            {
                if (level >= testLevel)
                {
                    levels.Add(testLevel);
                }
            }
            return levels.ToArray();
        }

        public static void Log(string message)
        {
            Log(message, _defaultLevel);
        }

        public static void Log(string message, LevelValue level)
        {
            switch(level)
            {
                case LevelValue.Warning:
                    _warningCount++;
                    break;
                case LevelValue.Error:
                    _errorCount++;
                    break;
            }

            if (level >= Level)
            {
                Console.ForegroundColor = GetColorFromLevel(level);
                Console.Write(message);
                Console.ResetColor();
            }

            LevelValue[] levels = GetLevels(level);
            foreach(LevelValue testLevel in levels)
            {
                if (_logFiles.ContainsKey(testLevel))
                {
                    File.AppendAllText(_logFiles[testLevel], message);
                }
                if (_htmlFiles.ContainsKey(testLevel))
                {
                    int indent = message.Length - message.TrimStart(new char[] { '\t' }).Length;
                    Debug.Assert(indent >= 0);
                    File.AppendAllText(_htmlFiles[testLevel], "<div style='padding-left:" + (indent * 15) + "px' class='" + level.ToString() + "'>" + message + "</div>");
                }
            }
        }

        public static void LogLine(string message)
        {
            LogLine(message, _defaultLevel);
        }

        public static void LogLine(string message, LevelValue level)
        {
            Log(message + "\r\n", level);
        }

        public static void LogLine(string[] messages)
        {
            LogLine(messages, _defaultLevel);
        }

        public static void LogLine(string[] messages, LevelValue level)
        {
            foreach(string message in messages)
            {
                LogLine(message, level);
            }
        }
    }
}
