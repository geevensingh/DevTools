using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utilities;

namespace GitStaleBranches
{
    class Program
    {
        static void Main(string[] args)
        {
#if DEBUG
            //Logger.AnnounceStartStopActions = true;
            Logger.Level = Logger.LevelValue.Verbose;
#endif

            bool localOnly = false;
            DateTime aMonthAgo = DateTime.Now.AddMonths(-1);
            string aMonthAgoString = aMonthAgo.ToShortDateString();

            string[] branches;
            if (localOnly)
            {
                branches = GitOperations.GetLocalBranches();
            }
            else
            {
                branches = GitOperations.GetRemoteBranches();
            }

            foreach(string branch in branches)
            {
                ProcessHelper proc = new ProcessHelper("git.exe", "log --since=" + aMonthAgoString + " " + branch);
                if (proc.Go().Length == 0)
                {
                    Logger.LogLine("No changes in " + branch, Logger.LevelValue.Warning);
                }
            }
        }
    }
}
