using System;
using System.Collections.Generic;

namespace Logging
{
    public class ConsoleLogger
    {
        private static ConsoleLogger _instance;
        private Logger _logger;

        public LogLevel MinLevel { get; set; }
        public bool IncludeTime { get; set; }
        public bool IncludeEventType { get; set; }

        public static ConsoleLogger Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ConsoleLogger();
                }

                return _instance;
            }
        }

        private ConsoleLogger()
        {
            if (_instance != null)
            {
                throw new InvalidOperationException("Not allowed to make a second ConsoleLogger");
            }

            this.MinLevel = LogLevel.Normal;
#if DEBUG
            this.MinLevel = LogLevel.Verbose;
#endif
            this.IncludeTime = true;
            this.IncludeEventType = true;

            this._logger = Logger.Instance;
            this._logger.OnLogEntryAdded += OnLogEntryAdded;
        }

        public void Shutdown()
        {
            _instance = null;
            this._logger.OnLogEntryAdded -= OnLogEntryAdded;
            this._logger = null;
        }

        private void OnLogEntryAdded(LogEntry logEntry)
        {
            if (logEntry == null)
            {
                throw new ArgumentNullException(nameof(logEntry));
            }

            if (logEntry.Level < this.MinLevel)
            {
                return;
            }

            List<string> strings = new List<string>();
            GetTimeString(logEntry, ref strings);
            GetIndexString(logEntry, ref strings);
            GetTypeString(logEntry, ref strings);
            strings.Add(logEntry.Text);

            Console.ForegroundColor = GetForegroundColor(logEntry);
            Console.Write(string.Join("    ", strings));
            Console.ResetColor();
        }

        private void GetIndexString(LogEntry logEntry, ref List<string> strings)
        {
            if (logEntry.Index == 0)
            {
                return;
            }

            strings.Add(new string(' ', (int)(4 * logEntry.Index)));
        }

        private void GetTimeString(LogEntry logEntry, ref List<string> strings)
        {
            if (!this.IncludeTime)
            {
                return;
            }

            if (logEntry.Time.Date != DateTime.Now.Date)
            {
                strings.Add(logEntry.Time.ToShortDateString());
            }

            strings.Add(logEntry.Time.ToLongTimeString());
        }

        private void GetTypeString(LogEntry logEntry, ref List<string> strings)
        {
            if (!this.IncludeEventType)
            {
                return;
            }

            switch (logEntry.LogType)
            {
                case LogType.Normal:
                    strings.Add("     ");
                    break;
                case LogType.Start:
                    strings.Add("Start");
                    break;
                case LogType.Stop:
                    strings.Add("Stop ");
                    break;
                default:
                    throw new ArgumentException($"Unknown log event type: {logEntry.LogType}", nameof(logEntry.LogType));
            }
        }

        private ConsoleColor GetForegroundColor(LogEntry logEntry)
        {
            switch (logEntry.Level)
            {
                case LogLevel.SuperChatty:
                case LogLevel.Verbose:
                    return ConsoleColor.Gray;
                case LogLevel.Normal:
                    return ConsoleColor.White;
                case LogLevel.Warning:
                    return ConsoleColor.Yellow;
                case LogLevel.Error:
                    return ConsoleColor.Red;
                default:
                    throw new ArgumentException($"Unknown level: {logEntry.Level}", nameof(logEntry.Level));
            }
        }
    }
}
