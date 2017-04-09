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

        public string[] Go(bool skipLines = false)
        {
            ProcessStartInfo psi = new ProcessStartInfo(_fileName, _arguments);
            if (_workingDirectory.Length > 0)
            {
                psi.WorkingDirectory = _workingDirectory;
            }
            string logEventName = "Process: " + _fileName.Substring(_fileName.LastIndexOf('\\') + 1) + " " + _arguments + " ( " + psi.WorkingDirectory + " )";
            Logger.Start(logEventName);

            psi.RedirectStandardOutput = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.UseShellExecute = false;
            Process proc = Process.Start(psi);
            StreamReader cmdOutput = proc.StandardOutput;
            List<string> output = new List<string>();
            Debug.Assert(!proc.HasExited);
            if (!skipLines)
            {
                string line = cmdOutput.ReadLine();
                while (line != null)
                {
                    output.Add(line);
                    line = cmdOutput.ReadLine();
                }
            }
            proc.WaitForExit();
            Debug.Assert(proc.HasExited);
            if (!skipLines)
            {
                string line = cmdOutput.ReadLine();
                while (line != null)
                {
                    output.Add(line);
                    line = cmdOutput.ReadLine();
                }
            }
            Logger.Stop(logEventName);
            return output.ToArray();
        }
    }
}
