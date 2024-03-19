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
            OldLogger.Level = OldLogger.LevelValue.Verbose;
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
                    OldLogger.Log(".", OldLogger.LevelValue.Verbose);
                    ProcessHelper proc = new ProcessHelper("git.exe", "log -n 1 --since=" + aMonthAgoString + " " + branch);
                    if (proc.Go().Length == 0)
                    {
                        string[] changeDescription = (new ProcessHelper("git.exe", "log --date=short --pretty=format:\"%an,%ae," + StringExtensions.TrimStart(branch, "origin/") + ",%ad\" -n 1 " + branch)).Go();
                        Debug.Assert(changeDescription.Length == 1);
                        lines.Add(changeDescription[0]);

                    }
                }
            }
            OldLogger.LogLine("", OldLogger.LevelValue.Verbose);
            lines.Sort();
            OldLogger.LogLine(lines.ToArray());

            OldLogger.LogLine(string.Empty);
            lines = new List<string>();

            foreach (string branch in branches)
            {
                if (branch.ToLower().StartsWith(@"origin/u/"))
                {
                    continue;
                }
                if (branch.ToLower().StartsWith(@"origin/user/"))
                {
                    continue;
                }
                if (branch.ToLower().StartsWith(@"origin/users/"))
                {
                    continue;
                }
                if (branch.ToLower().StartsWith(@"origin/feature/"))
                {
                    continue;
                }
                if (branch.ToLower().StartsWith(@"origin/features/"))
                {
                    continue;
                }
                if (branch.ToLower().StartsWith(@"origin/release/"))
                {
                    continue;
                }
                if (branch.Contains(" -> "))
                {
                    continue;
                }

                OldLogger.LogLine(branch);

                string[] changeDescription = (new ProcessHelper("git.exe", "log --date=short --pretty=format:\"%an (%ae) was the last person to touch " + StringExtensions.TrimStart(branch, "origin/") + " on %ad\" -n 1 " + branch)).Go();
                Debug.Assert(changeDescription.Length == 1);
                lines.Add(changeDescription[0]);
            }

            OldLogger.LogLine("", OldLogger.LevelValue.Verbose);
            lines.Sort();
            OldLogger.LogLine(lines.ToArray());

        }
    }
}
