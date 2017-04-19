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

        private string _fileName = "";
        private string _arguments = "";
        private string _workingDirectory = "";

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
            ProcessStartInfo psi = new ProcessStartInfo(_fileName, _arguments);
            if (_workingDirectory.Length > 0)
            {
                psi.WorkingDirectory = _workingDirectory;
            }
            string logEventName = "Process: " + _fileName.Substring(_fileName.LastIndexOf('\\') + 1) + " " + _arguments;
            if (!string.IsNullOrEmpty(psi.WorkingDirectory))
            {
                logEventName += " ( " + psi.WorkingDirectory + " )";
            }
            Logger.Start(logEventName);

            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.UseShellExecute = false;
            Process proc = Process.Start(psi);
            StreamReader cmdOutput = proc.StandardOutput;
            StreamReader cmdError = proc.StandardError;
            List<string> output = new List<string>();
            List<string> errors = new List<string>();
            while (!proc.HasExited)
            {
                string x = cmdOutput.ReadToEnd();
                if (!string.IsNullOrEmpty(x))
                {
                    output.AddRange(x.Replace("\r\n", "\n").Replace("\r", "\n").Split(new string[] { "\n" }, StringSplitOptions.None));
                }
                x = cmdError.ReadToEnd();
                if (!string.IsNullOrEmpty(x))
                {
                    errors.AddRange(x.Replace("\r\n", "\n").Replace("\r", "\n").Split(new string[] { "\n" }, StringSplitOptions.None));
                }
            }

            if (output.Count > 0 && string.IsNullOrEmpty(output[output.Count - 1]))
            {
                output.RemoveAt(output.Count - 1);
            }
            if (errors.Count > 0 && string.IsNullOrEmpty(errors[errors.Count - 1]))
            {
                errors.RemoveAt(errors.Count - 1);
            }

            _standardError = errors.ToArray();
            Logger.Stop(logEventName);
            return output.ToArray();
        }

        private string[] _standardError = new string[] { };
        public string[] StandardError
        {
            get { return _standardError; }
        }
    }
}
