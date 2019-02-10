using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace Logging
{
    public class Logger
    {

        private readonly List<string> _pending = new List<string>();
        private uint _nextIndex = 0;
        private readonly List<LogEntry> _logEntries = new List<LogEntry>();

        private static Logger _instance;

        public delegate void OnLogEntryAddedHandler(LogEntry logEntry);
        public event OnLogEntryAddedHandler OnLogEntryAdded;

        public static Logger Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new Logger();
                }

                return _instance;
            }
        }

        private Logger()
        {
            this.LogLevel = LogLevel.Normal;
        }

        public LogLevel LogLevel { get; set; }

        public void Start(string description)
        {
            this.Start(description, uniqueId: description, this.LogLevel);
        }

        public void Start(string description, string uniqueId, LogLevel logLevel)
        {
            Debug.Assert(!this._pending.Contains(uniqueId));
            this._pending.Add(uniqueId);
            this.AddLogEntry(LogType.Start, EnsureLineEnding(description), uniqueId, logLevel);
        }
        public void Stop(string description)
        {
            this.Stop(description, uniqueId: description, this.LogLevel);
        }

        public void Stop(string description, string uniqueId, LogLevel logLevel)
        {
            Debug.Assert(this._pending.Contains(uniqueId));
            this.AddLogEntry(LogType.Stop, EnsureLineEnding(description), uniqueId, logLevel);
            this._pending.Remove(uniqueId);
        }

        public void Log(string description)
        {
            this.Log(description, this.LogLevel);
        }

        public void Log(string description, LogLevel logLevel)
        {
            this.AddLogEntry(LogType.Normal, description, Guid.NewGuid().ToString(), logLevel);
        }

        public void LogLine(string description = null)
        {
            this.LogLine(description, this.LogLevel);
        }

        public void LogLine(string description, LogLevel logLevel)
        {
            this.Log(EnsureLineEnding(description), logLevel);
        }

        private static string EnsureLineEnding(string description)
        {
            if (string.IsNullOrEmpty(description) || !description.EndsWith("\r\n"))
            {
                description += "\r\n";
            }

            return description;
        }

        private void AddLogEntry(LogType logType, string description, string uniqueId, LogLevel logLevel)
        {
            uint thisIndex = this._nextIndex;
            if (logType == LogType.Stop)
            {
                Debug.Assert(this._logEntries.Count(e => e.UniqueId == uniqueId && e.LogType == LogType.Start) == 1);
                Debug.Assert(this._logEntries.Count(e => e.UniqueId == uniqueId && e.LogType == LogType.Stop) == 0);

                Debug.Assert(this._nextIndex > 0);
                this._nextIndex--;
                thisIndex--;
            }
            else if (logType == LogType.Start)
            {
                Debug.Assert(this._logEntries.Count(e => e.UniqueId == uniqueId) == 0);

                this._nextIndex++;
            }

            LogEntry logEntry = new LogEntry()
            {
                Level = logLevel,
                Index = thisIndex,
                Text = description,
                LogType = logType,
                Time = DateTime.Now,
                UniqueId = uniqueId,
            };
            this._logEntries.Add(logEntry);
            this.OnLogEntryAdded?.Invoke(logEntry);
        }
    }
}
