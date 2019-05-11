using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace Utilities
{
    public class ProcessHelper : IDisposable
    {
        public ProcessHelper(string fileName, string arguments)
        {
            Debug.Assert(fileName.Length > 0);
            _fileName = fileName;
            _arguments = arguments;
        }

        private int _exitCode = -1;
        private string _fileName = "";
        private string _arguments = "";

        private struct OutputInfo
        {
            public OutputInfo(string line, LineType type)
            {
                _line = line;
                _type = type;
            }
            public string Line => _line;
            public bool IsEmpty => string.IsNullOrEmpty(_line);
            public enum LineType
            {
                Output,
                Error
            };
            public LineType Type => _type;

            private string _line;
            private LineType _type;
        }
        private List<OutputInfo> _output = new List<OutputInfo>();
        private bool _processOver = false;

        public string WorkingDirectory { get; set; } = "";

        public string CommandLine => string.Join(" ", new string[] { _fileName, _arguments });

        public string[] Go()
        {
            return Go(OldLogger.LevelValue.SuperChatty);
        }

        public string[] Go(OldLogger.LevelValue logLevel)
        {
            Process process = new Process();
            process.StartInfo.FileName = _fileName;
            process.StartInfo.Arguments = _arguments;
            if (!string.IsNullOrEmpty(WorkingDirectory))
            {
                process.StartInfo.WorkingDirectory = WorkingDirectory;
            }
            string logEventName = "Process: " + _fileName.Substring(_fileName.LastIndexOf('\\') + 1) + " " + _arguments;
            if (!string.IsNullOrEmpty(process.StartInfo.WorkingDirectory))
            {
                logEventName += " ( " + process.StartInfo.WorkingDirectory + " )";
            }
            OldLogger.Start(logEventName, logLevel);

            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.UseShellExecute = false;
            process.OutputDataReceived += Process_OutputDataReceived;
            process.ErrorDataReceived += Process_ErrorDataReceived;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            _exitCode = process.ExitCode;
            _processOver = true;

            process.OutputDataReceived -= Process_OutputDataReceived;
            process.ErrorDataReceived -= Process_ErrorDataReceived;

            OldLogger.Stop(logEventName, output: GetAllOutput(true), level: logLevel);
            return GetAllOutput(false);
        }
        private System.Threading.Mutex _outputMutex = new System.Threading.Mutex();
        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            Debug.Assert(!_processOver);
            if (e.Data != null)
            {
                OutputInfo info = new OutputInfo(e.Data, OutputInfo.LineType.Error);
                _outputMutex.WaitOne();
                _output.Add(info);
                _outputMutex.ReleaseMutex();
            }
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            Debug.Assert(!_processOver);
            if (e.Data != null)
            {
                OutputInfo info = new OutputInfo(e.Data, OutputInfo.LineType.Output);
                _outputMutex.WaitOne();
                _output.Add(info);
                _outputMutex.ReleaseMutex();
            }
        }

        public string[] StandardError => FilterOutput(OutputInfo.LineType.Error);
        public string[] StandardOutput => FilterOutput(OutputInfo.LineType.Output);
        public string[] AllOutput => GetAllOutput(false);

        public string[] RawOutput
        {
            get
            {
                _outputMutex.WaitOne();
                IEnumerable<string> result = _output.Select(x => x.Line);
                _outputMutex.ReleaseMutex();
                return result.ToArray();
            }
        }

        private string[] GetAllOutput(bool withPrefix)
        {
            List<string> result = new List<string>();
            _outputMutex.WaitOne();
            foreach (OutputInfo info in _output)
            {
                if (info.IsEmpty)
                {
                    continue;
                }

                string line = info.Line;
                if (withPrefix)
                {
                    switch (info.Type)
                    {
                        case OutputInfo.LineType.Output:
                            line = "  " + line;
                            break;
                        case OutputInfo.LineType.Error:
                            line = "! " + line;
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                }
                result.Add(line);
            }
            _outputMutex.ReleaseMutex();
            return result.ToArray();
        }

        private string[] FilterOutput(OutputInfo.LineType filter)
        {
            List<string> result = new List<string>();
            _outputMutex.WaitOne();
            foreach (OutputInfo info in _output)
            {
                if (info.Type == filter)
                {
                    result.Add(info.Line);
                }
            }
            _outputMutex.ReleaseMutex();
            return result.ToArray();
        }

        public void Dispose()
        {
            this.Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _outputMutex.Dispose();
                _outputMutex = null;
            }
        }

        public int ExitCode { get => _exitCode; }
    }
}
