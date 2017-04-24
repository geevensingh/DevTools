using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utilities;
using System.Diagnostics;

namespace GitNightly
{
    class Program
    {
        static void Main(string[] args)
        {
#if DEBUG
            Logger.Level = Logger.LevelValue.Verbose;
#endif
            for (int ii = 0; ii < args.Length; ii++)
            {
                string arg = args[ii].ToLower();
                switch(arg)
                {
                    case "/v":
                    case "/verbose":
                        Logger.Level = Logger.LevelValue.Verbose;
                        break;
                    case "/log":
                        Logger.LogFile = args[++ii];
                        break;
                    default:
                        Console.WriteLine("Unknown argument: " + arg);
                        PrintUsage();
                        return;
                }
            }

            GitStatus originalStatus = GitOperations.GetStatus();
            String originalBranch = originalStatus.Branch;
            Logger.LogLine("Started in " + originalBranch);
            if (originalStatus.AnyChanges)
            {
                GitOperations.Stash();
            }

            string[] releaseForkPoints = GitOperations.GetFirstChanges(GitOperations.GetReleaseBranchNames());

            GitOperations.SwitchBranch("master");
            GitOperations.PullCurrentBranch();

            string[] branches = GitOperations.GetLocalBranches();
            foreach (string branch in branches)
            {
                if (branch == "master")
                {
                    continue;
                }

                string releaseForkPoint = GitOperations.BranchContains(branch, releaseForkPoints);
                if (!string.IsNullOrEmpty(releaseForkPoint))
                {
                    Logger.LogLine("Ignoring branch " + branch + " because it comes from a release branch", Logger.LevelValue.Warning);
                    Logger.LogLine("\tBranch contains " + releaseForkPoint, Logger.LevelValue.Normal);
                    continue;
                }

                GitOperations.SwitchBranch(branch);
                GitStatus status = GitOperations.GetStatus();
                if (status.RemoteChanges == "remote-gone")
                {
                    Logger.LogLine("Remote branch is gone for " + branch, Logger.LevelValue.Warning);
                    GitOperations.SwitchBranch("master");
                    GitOperations.DeleteBranch(branch, force: false);
                    if (branch == originalBranch)
                    {
                        originalBranch = "master";
                    }
                }
                else
                {
                    GitOperations.MergeFromBranch("master");
                }
            }

            GitOperations.SwitchBranch(originalBranch);
            if (originalStatus.AnyChanges)
            {
                GitOperations.StashPop();
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine(@"
    Valid arguments:
        /v
        /verbose
");
        }
    }
}
