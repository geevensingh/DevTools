using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utilities;

namespace GitPrompt
{
    class Program
    {
        static void Main(string[] args)
        {
            OldLogger.AnnounceStartStopActions = false;
#if DEBUG
            OldLogger.AnnounceStartStopActions = true;
#endif

            GitStatus status = GitStatus.Get();
            string root = Environment.GetEnvironmentVariable("REPO_ROOT");
            OldLogger.LogLine(String.Join(" ", new string[] { root, status.Branch, status.RemoteChanges, status.AllLocalChanges }));
        }
    }
}
