using System;

namespace Logging
{
    public class LogEntry
    {
        public LogLevel Level { get; set; }
        public uint Index { get; set; }
        public string Text { get; set; }
        public LogType LogType { get; set; }
        public DateTime Time { get; set; }
        public string UniqueId { get; set; }
    }
}
