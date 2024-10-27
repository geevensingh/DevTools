using System;
using System.Diagnostics;

namespace Logging
{
    public class ScopeLogger : IDisposable
    {
        private readonly string scopeName;
        private DateTime lastLogTime;
        private Stopwatch stopwatch;

        public ScopeLogger(
            [System.Runtime.CompilerServices.CallerMemberName] string scopeName = null)
        {
            if (string.IsNullOrEmpty(scopeName))
            {
                scopeName = Guid.NewGuid().ToString();
            }

            this.scopeName = scopeName;
            this.stopwatch = Stopwatch.StartNew();

            this.Start();
        }

        public void Dispose()
        {
            this.Finish();
        }

        public void LogTimeSinceLastLog(string label)
        {
            this.InternalLog($"{label} duration (s): {(DateTime.UtcNow - this.lastLogTime).TotalSeconds}");
        }

        public void LogObject(string objectName, object objectToSerialize)
        {
            this.Log($"{objectName} = {Serialize(objectToSerialize)}");
        }

        public void LogObject(string stringName, string stringContent)
        {
            this.Log($"{stringName} = {stringContent ?? "null"}");
        }

        public void LogObject(string dateTimeName, DateTime dateTime)
        {
            this.Log($"{dateTimeName} = {dateTime:O}");
        }

        public T LogReturn<T>(T objectToSerialize)
        {
            this.Log($"returning {Serialize(objectToSerialize)}");
            return objectToSerialize;
        }

        public void Log(string message)
        {
            // Extra spaces here are designed to align with Start/End logging below.
            this.InternalLog($"         {this.scopeName} : {message}");
        }

        private void Start()
        {
            this.InternalLog($"Starting {this.scopeName}");
        }

        private void Finish()
        {
            this.InternalLog($"Ending   {this.scopeName} \t duration (ms): {this.stopwatch.ElapsedMilliseconds}");
        }

        private void InternalLog(string message)
        {
            Logger.Instance.LogLine(message);
            this.lastLogTime = DateTime.UtcNow;
        }

        private static string Serialize(object objectToSerialize)
        {
            if (objectToSerialize == null)
            {
                return "null";
            }

            return objectToSerialize.ToString();
        }
    }
}