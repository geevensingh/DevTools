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

        public string[] Go()
        {
            ProcessStartInfo psi = new ProcessStartInfo(_fileName, _arguments);
            if (_workingDirectory.Length > 0)
            {
                psi.WorkingDirectory = _workingDirectory;
            }
            string logEventName = "Process: " + _fileName.Substring(_fileName.LastIndexOf('\\') + 1) + " " + _arguments + " ( " + psi.WorkingDirectory + " )";
            Logger.Start(logEventName);

            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.UseShellExecute = false;
            Process proc = Process.Start(psi);
            StreamReader cmdOutput = proc.StandardOutput;
            List<string> output = new List<string>();
            Debug.Assert(!proc.HasExited);
            string line = cmdOutput.ReadLine();
            while (line != null)
            {
                output.Add(line);
                line = cmdOutput.ReadLine();
            }
            proc.WaitForExit();
            Debug.Assert(proc.HasExited);
            line = cmdOutput.ReadLine();
            while (line != null)
            {
                output.Add(line);
                line = cmdOutput.ReadLine();
            }

            StreamReader cmdError = proc.StandardError;
            List<string> errors = new List<string>();
            line = cmdError.ReadLine();
            while (line != null)
            {
                errors.Add(line);
                line = cmdError.ReadLine();
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
