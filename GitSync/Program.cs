using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utilities;
using System.Diagnostics;

namespace GitSync
{
    class Program
    {
        static int Main(string[] args)
        {
            bool forceSync = false;
            bool forceDelete = false;
#if DEBUG
            Logger.Level = Logger.LevelValue.Verbose;
            Logger.AnnounceStartStopActions = true;
#endif
            for (int ii = 0; ii < args.Length; ii++)
            {
                string arg = args[ii].ToLower();
                switch(arg)
                {
                    case "/v":
                    case "/verbose":
                        Logger.AnnounceStartStopActions = true;
                        Logger.Level = Logger.LevelValue.Verbose;
                        break;
                    case "/log":
                        Logger.AddLogFile(args[++ii]);
                        break;
                    case "/html":
                        Logger.AddHTMLLogFile(args[++ii]);
                        break;
                    case "/vlog":
                        Logger.AnnounceStartStopActions = true;
                        Logger.AddLogFile(args[++ii], Logger.LevelValue.Verbose);
                        break;
                    case "/fs":
                        forceSync = true;
                        break;
                    case "/fd":
                        forceDelete = true;
                        break;
                    default:
                        Console.WriteLine("Unknown argument: " + arg);
                        PrintUsage();
                        return -1;
                }
            }

            string verboseLogFile = Logger.VerboseLogPath;
            if (!string.IsNullOrEmpty(verboseLogFile))
            {
                Logger.LogLine("Verbose log path: " + verboseLogFile);
            }

            GitOperations.FetchAll();

            string[] releaseBranches = GitOperations.GetReleaseBranchNames();
            string[] releaseForkPoints = GitOperations.GetFirstChanges(releaseBranches);
            bool uniqueReleaseBranches = (releaseBranches.Length == releaseForkPoints.Length);
            if (!uniqueReleaseBranches)
            {
                Logger.LogLine("At least one release branch is not unique.", Logger.LevelValue.Warning);
            }
            if (!uniqueReleaseBranches && !forceSync)
            {
                Logger.LogLine("Without knowing that a branch is unique, we can't update any branches.", Logger.LevelValue.Error);
            }
            else
            {
                GitStatus originalStatus = GitOperations.GetStatus();
                String originalBranch = originalStatus.Branch;
                Logger.LogLine("Started in " + originalBranch);
                if (originalStatus.AnyChanges)
                {
                    if (!GitOperations.Stash())
                    {
                        Logger.LogLine("Unable to stash the current work.  Cannot continue.", Logger.LevelValue.Error);
                        return -1;
                    }
                }

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
                        GitOperations.DeleteBranch(branch, force: forceDelete);
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

            Logger.FlushLogs();
            return (int)Logger.WarningCount;
        }

        private static void PrintUsage()
        {
            Console.WriteLine(@"
    Valid arguments:
        /v
        /verbose

        /log <log-file>
");
        }
    }
}
