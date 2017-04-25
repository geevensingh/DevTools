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
        private List<string> _standardOutput = new List<string>();
        private List<string> _standardError = new List<string>();
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

            if (_standardOutput.Count > 0 && string.IsNullOrEmpty(_standardOutput[_standardOutput.Count - 1]))
            {
                _standardOutput.RemoveAt(_standardOutput.Count - 1);
            }
            if (_standardError.Count > 0 && string.IsNullOrEmpty(_standardError[_standardError.Count - 1]))
            {
                _standardError.RemoveAt(_standardError.Count - 1);
            }

            Logger.Stop(logEventName);
            return _standardOutput.ToArray();
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            Debug.Assert(!_processOver);
            _standardError.Add(e.Data);
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            Debug.Assert(!_processOver);
            _standardOutput.Add(e.Data);
        }

        public string[] StandardError
        {
            get { return _standardError.ToArray(); }
        }
        public string[] StandardOutput
        {
            get { return _standardOutput.ToArray(); }
        }

        public int ExitCode { get => _exitCode; }
    }
}
