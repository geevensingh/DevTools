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
            bool forceDelete = false;
#if DEBUG
            Logger.Level = Logger.LevelValue.Verbose;
            Logger.AnnounceStartStopActions = true;
#endif
            for (int ii = 0; ii < args.Length; ii++)
            {
                string arg = args[ii].ToLower();
                switch (arg)
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
                    case "/fd":
                        forceDelete = true;
                        break;
                    default:
                        Console.WriteLine("Unknown argument: " + arg);
                        PrintUsage();
                        Logger.FlushLogs();
                        return (int)Logger.WarningCount;
                }
            }

            string verboseLogFile = Logger.VerboseLogPath;
            if (!string.IsNullOrEmpty(verboseLogFile))
            {
                Logger.LogLine("Verbose log path: " + verboseLogFile);
            }

            string[] localBranches = GitOperations.GetLocalBranches();

            GitOperations.FetchAll();

            List<string> missingBasedOn = new List<string>();
            Dictionary<string, string> branchBasedOn = new Dictionary<string, string>();
            foreach (string branch in localBranches)
            {
                if (branch == "master")
                {
                    branchBasedOn.Add(branch, branch);
                    continue;
                }
                string basedOn = GitOperations.GetBranchBase(branch);
                if (string.IsNullOrEmpty(basedOn))
                {
                    missingBasedOn.Add(branch);
                }
                else
                {
                    branchBasedOn.Add(branch, basedOn);
                }
            }

            string[] releaseBranches = GitOperations.GetReleaseBranchNames();   
            string[] releaseForkPoints = GitOperations.GetFirstChanges(releaseBranches);
            bool allReleaseBranchesAreUnique = (releaseBranches.Length == releaseForkPoints.Length);
            if (!allReleaseBranchesAreUnique && missingBasedOn.Count > 0)
            {
                for (int ii = 0; ii < missingBasedOn.Count; ii++)
                {
                    string branch = missingBasedOn[ii];
                    string releaseForkPoint = GitOperations.BranchContains(branch, releaseForkPoints);
                    if (!string.IsNullOrEmpty(releaseForkPoint))
                    {
                        missingBasedOn.RemoveAt(ii--);
                    }
                }
            }
            else
            {
                // If we know the fork points of all release branches, then we don't need the based-on information.
                missingBasedOn.Clear();
            }

            GitStatus originalStatus = GitOperations.GetStatus();
            String originalBranch = originalStatus.Branch;
            Logger.LogLine("Started in " + originalBranch);
            if (originalStatus.AnyChanges)
            {
                if (!GitOperations.Stash())
                {
                    Logger.LogLine("Unable to stash the current work.  Cannot continue.", Logger.LevelValue.Error);
                    Logger.FlushLogs();
                    return (int)Logger.WarningCount;
                }
            }

            GitOperations.SwitchBranch("master");
            GitOperations.PullCurrentBranch();

            foreach (string branch in localBranches)
            {
                if (branch == "master")
                {
                    continue;
                }

                if (missingBasedOn.Contains(branch))
                {
                    Logger.LogLine("Ignoring branch: " + branch, Logger.LevelValue.Warning);
                    Logger.LogLine("\tUnknown branch source (based-on) and there is currently an unforked release branch.");
                    Logger.LogLine("\tIf this is based on master, run the following command:");
                    Logger.LogLine("\t\tgit config branch." + branch + ".basedon master");
                    continue;
                }

                string releaseForkPoint = GitOperations.BranchContains(branch, releaseForkPoints);
                if (!string.IsNullOrEmpty(releaseForkPoint))
                {
                    Logger.LogLine("Ignoring release branch: " + branch);
                    Logger.LogLine("\tBranch contains " + releaseForkPoint, Logger.LevelValue.Verbose);
                    continue;
                }

                if (branchBasedOn[branch].Contains("release"))
                {
                    Logger.LogLine("Ignoring release branch: " + branch);
                    Logger.LogLine("\tBranch based on " + branchBasedOn[branch], Logger.LevelValue.Verbose);
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
