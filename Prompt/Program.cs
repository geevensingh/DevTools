using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utilities;

namespace Prompt
{
    class Program
    {
        static void Main(string[] args)
        {
            Logger.AnnounceStartStopActions = false;
            //Logger.LogLine(GitOperations.GetCurrentBranchName());
            GitStatus status = GitStatus.GetStatus();
            //Logger.LogLine(status.ToString());

            string root = Environment.GetEnvironmentVariable("REPO_ROOT");
            string branch = GitOperations.GetCurrentBranchName();
            string changes = status.AnyChanged ? status.ToString() : string.Empty;

            Logger.LogLine(String.Join(" ", new string[] { root, branch, changes }));
        }
    }
}
