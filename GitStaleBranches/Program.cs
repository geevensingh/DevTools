using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utilities;
using System.Diagnostics;

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

            List<string> lines = new List<string>();
            foreach(string branch in branches)
            {
                if (!branch.Contains(" -> "))
                {
                    Logger.Log(".", Logger.LevelValue.Verbose);
                    ProcessHelper proc = new ProcessHelper("git.exe", "log -n 1 --since=" + aMonthAgoString + " " + branch);
                    if (proc.Go().Length == 0)
                    {
                        string[] changeDescription = (new ProcessHelper("git.exe", "log --date=short --pretty=format:\" %an (%ae) was the last person to touch " + StringHelper.TrimStart(branch, "origin/") + " on %ad\" -n 1 " + branch)).Go();
                        Debug.Assert(changeDescription.Length == 1);
                        lines.Add(changeDescription[0]);

                    }
                }
            }
            Logger.LogLine("", Logger.LevelValue.Verbose);
            lines.Sort();
            Logger.LogLine(lines.ToArray());
        }
    }
}
