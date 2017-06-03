using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;

namespace Utilities
{
    public class ProcessHelper
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
        private string _workingDirectory = "";
        private struct OutputInfo
        {
            public OutputInfo(string line, LineType type)
            {
                Debug.Assert(line != null);
                _line = line;
                _type = type;
            }
            public string Line
            {
                get { return _line; }
            }
            public enum LineType
            {
                Output,
                Error
            };
            public LineType Type
            {
                get { return _type; }
            }

            private string _line;
            private LineType _type;
        }
        private List<OutputInfo> _output = new List<OutputInfo>();
        private bool _processOver = false;

        public string WorkingDirectory
        {
            get { return _workingDirectory; }
            set { _workingDirectory = value; }
        }

        public string CommandLine
        {
            get { return string.Join(" ", new string[] { _fileName, _arguments }); }
        }

        public string[] Go()
        {
            Process process = new Process();
            process.StartInfo.FileName = _fileName;
            process.StartInfo.Arguments = _arguments;
            if (!string.IsNullOrEmpty(_workingDirectory))
            {
                process.StartInfo.WorkingDirectory = _workingDirectory;
            }
            string logEventName = "Process: " + _fileName.Substring(_fileName.LastIndexOf('\\') + 1) + " " + _arguments;
            if (!string.IsNullOrEmpty(process.StartInfo.WorkingDirectory))
            {
                logEventName += " ( " + process.StartInfo.WorkingDirectory + " )";
            }
            Logger.Start(logEventName);

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

            for (int ii = 0; ii < _output.Count; ii++)
            {
                if (_output[ii].Line == null)
                {
                    _output.RemoveAt(ii--);
                }
            }

            Logger.Stop(logEventName, output: GetAllOutput(true));
            return GetAllOutput(false);
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            Debug.Assert(!_processOver);
            if (e.Data != null)
            {
                _output.Add(new OutputInfo(e.Data, OutputInfo.LineType.Error));
            }
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            Debug.Assert(!_processOver);
            if (e.Data != null)
            {
                _output.Add(new OutputInfo(e.Data, OutputInfo.LineType.Output));
            }
        }

        public string[] StandardError
        {
            get { return FilterOutput(OutputInfo.LineType.Error); }
        }
        public string[] StandardOutput
        {
            get { return FilterOutput(OutputInfo.LineType.Output); }
        }
        public string[] AllOutput
        {
            get { return GetAllOutput(false); }
        }

        private string[] GetAllOutput(bool withPrefix)
        {
            List<string> result = new List<string>();
            foreach (OutputInfo info in _output)
            {
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
            return result.ToArray();
        }

        private string[] FilterOutput(OutputInfo.LineType filter)
        {
            List<string> result = new List<string>();
            foreach (OutputInfo info in _output)
            {
                if (info.Type == filter)
                {
                    result.Add(info.Line);
                }
            }
            return result.ToArray();
        }

        public int ExitCode { get => _exitCode; }
    }
}
