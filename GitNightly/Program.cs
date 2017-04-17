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
            foreach (string arg in args)
            {
                switch(arg.ToLower())
                {
                    case "/v":
                    case "/verbose":
                        Logger.Level = Logger.LevelValue.Verbose;
                        break;
                    default:
                        Console.WriteLine("Unknown argument: " + arg);
                        PrintUsage();
                        return;
                }
            }

            GitStatus originalStatus = GitOperations.GetStatus();
            String originalBranch = originalStatus.Branch;
            string root = Environment.GetEnvironmentVariable("REPO_ROOT");
            if (originalStatus.AnyChanges)
            {
                GitOperations.Stash();
            }

            string[] releaseForkPoints = GitOperations.GetReleaseForkPoints();

            GitOperations.SwitchBranch("master");
            GitOperations.PullCurrentBranch();
            string[] branches = GitOperations.GetLocalBranches();
            foreach (string branch in branches)
            {
                if (branch == "master")
                {
                    continue;
                }

                GitOperations.SwitchBranch(branch);
                if (GitOperations.BranchContains(branch, releaseForkPoints))
                {
                    Logger.LogLine("Ignoring branch " + branch + " because it comes from a release branch", Logger.LevelValue.Warning);
                    continue;
                }

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
