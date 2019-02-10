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
            OldLogger.Level = OldLogger.LevelValue.Verbose;
            OldLogger.AnnounceStartStopActions = true;
#endif
            for (int ii = 0; ii < args.Length; ii++)
            {
                string arg = args[ii].ToLower();
                switch (arg)
                {
                    case "/v":
                    case "/verbose":
                        OldLogger.AnnounceStartStopActions = true;
                        OldLogger.Level = OldLogger.LevelValue.Verbose;
                        break;
                    case "/log":
                        OldLogger.AddLogFile(args[++ii]);
                        break;
                    case "/html":
                        OldLogger.AddHTMLLogFile(args[++ii]);
                        break;
                    case "/vlog":
                        OldLogger.AnnounceStartStopActions = true;
                        OldLogger.AddLogFile(args[++ii], OldLogger.LevelValue.Verbose);
                        break;
                    case "/fd":
                        forceDelete = true;
                        break;
                    default:
                        Console.WriteLine("Unknown argument: " + arg);
                        PrintUsage();
                        OldLogger.FlushLogs();
                        return (int)OldLogger.WarningCount;
                }
            }

            string verboseLogFile = OldLogger.VerboseLogPath;
            if (!string.IsNullOrEmpty(verboseLogFile))
            {
                OldLogger.LogLine("Verbose log path: " + verboseLogFile);
            }

            OldLogger.Log("Fetching... ");
            GitOperations.FetchAll();
            OldLogger.LogLine("done");

            OldLogger.LogLine(@"Examining local branches...");

            List<string> localBranches = new List<string>(GitOperations.GetLocalBranches());
            string masterBranch = "master";
            bool hasLocalMaster = localBranches.Contains(masterBranch);
            if (!hasLocalMaster)
            {
                masterBranch = "origin/master";
            }

            List<string> localBranchesWithoutRemote = new List<string>();
            List<string> remoteBranches = new List<string>(GitOperations.GetRemoteBranches());
            foreach(string branch in localBranches)
            {
                if (!remoteBranches.Contains(branch) && !remoteBranches.Contains("origin/" + branch) && !branch.Contains("HEAD"))
                {
                    OldLogger.LogLine("Remote branch is gone for " + branch, OldLogger.LevelValue.Warning);
                    localBranchesWithoutRemote.Add(branch);
                }
            }

            Dictionary<string, string> branchBasedOn = new Dictionary<string, string>();
            foreach (string branch in localBranches)
            {
                if (branch == masterBranch)
                {
                    continue;
                }
                string basedOn = GitOperations.GetBranchBase(branch);
                branchBasedOn.Add(branch, basedOn);
            }

            List<string> missingBasedOn = new List<string>();
            foreach (string branch in branchBasedOn.Keys)
            {
                if (string.IsNullOrEmpty(branchBasedOn[branch]))
                {
                    OldLogger.LogLine(@"missing based on: " + branch, OldLogger.LevelValue.Verbose);
                    missingBasedOn.Add(branch);
                }
            }

            for (int ii = 0; ii < missingBasedOn.Count; ii++)
            {
                string branch = missingBasedOn[ii];
                if (GitOperations.IsReleaseBranchName(branch))
                {
                    missingBasedOn.RemoveAt(ii--);
                    OldLogger.LogLine(@"not worried about release branch: " + branch, OldLogger.LevelValue.Verbose);
                }
            }

            if (missingBasedOn.Count > 0)
            {
                string[] releaseBranches = GitOperations.GetReleaseBranchNames();
                Dictionary<string, string> releaseForkPoints = GitOperations.GetUniqueCommits(masterBranch, releaseBranches);
                for (int ii = 0; ii < missingBasedOn.Count; ii++)
                {
                    string branch = missingBasedOn[ii];
                    string basedOnBranch = GitOperations.BranchContains(branch, releaseForkPoints);
                    if (string.IsNullOrEmpty(basedOnBranch))
                    {
                        OldLogger.LogLine("Ignoring " + branch, OldLogger.LevelValue.Warning);
                        OldLogger.LogLine("Unable to determine the parent branch.  If this is based on " + masterBranch + ", run the following command:");
                        OldLogger.LogLine("\tgit config branch." + branch + ".basedon " + masterBranch);
                        branchBasedOn.Remove(branch);
                    }
                    else
                    {
                        OldLogger.LogLine(branch + @" seems to be based on " + basedOnBranch);
                        OldLogger.LogLine(@"If so, run the following command for faster runs in the future:");
                        OldLogger.LogLine("\tgit config branch." + branch + ".basedon " + basedOnBranch);
                        Debug.Assert(string.IsNullOrEmpty(branchBasedOn[branch]));
                        branchBasedOn[branch] = basedOnBranch;
                        missingBasedOn.Remove(branch);
                    }
                }
            }

            GitStatus originalStatus = GitOperations.GetStatus();
            String originalBranch = originalStatus.Branch;
            OldLogger.LogLine("\r\nStarted in " + originalBranch);
            if (originalStatus.AnyChanges)
            {
                if (!GitOperations.Stash())
                {
                    OldLogger.LogLine("Unable to stash the current work.  Cannot continue.", OldLogger.LevelValue.Error);
                    OldLogger.FlushLogs();
                    return (int)OldLogger.WarningCount;
                }
            }

            ProcessHelper failureProc = null;
            foreach (string branch in localBranchesWithoutRemote)
            {
                if (GitOperations.GetCurrentBranchName() == branch)
                {
                    if (!GitOperations.SwitchBranch(masterBranch, out failureProc))
                    {
                        OldLogger.LogLine("Unable to switch branches", OldLogger.LevelValue.Warning);
                        OldLogger.LogLine(failureProc.AllOutput, OldLogger.LevelValue.Normal);
                        continue;
                    }
                }

                if (GitOperations.DeleteBranch(branch, force: forceDelete) && (branch == originalBranch))
                {
                    originalBranch = masterBranch;
                }

                if (branchBasedOn.ContainsKey(branch))
                {
                    branchBasedOn.Remove(branch);
                }
            }

            if (hasLocalMaster)
            {
                PullBranch("master");
            }

            foreach (string branch in branchBasedOn.Keys)
            {
                Debug.Assert(!missingBasedOn.Contains(branch));
                OldLogger.LogLine(string.Empty);

                PullBranch(branch);

                string parentBranch = branchBasedOn[branch];
                if (!string.IsNullOrEmpty(parentBranch) && !localBranches.Contains(parentBranch))
                {
                    parentBranch = "origin/" + parentBranch;
                }
                MergeBranch(branch, parentBranch);
            }
            OldLogger.LogLine(string.Empty);

            if (!GitOperations.SwitchBranch(originalBranch, out failureProc))
            {
                OldLogger.LogLine("Unable to switch branches", OldLogger.LevelValue.Error);
                OldLogger.LogLine(failureProc.AllOutput, OldLogger.LevelValue.Warning);
                OldLogger.FlushLogs();
                return (int)OldLogger.WarningCount;
            }

            if (originalStatus.AnyChanges)
            {
                GitOperations.StashPop();
            }

            OldLogger.FlushLogs();
            return (int)OldLogger.WarningCount;
        }

        private static void PullBranch(string localBranch)
        {
            if (BranchCheck(localBranch, "origin/" + localBranch))
            {
                GitOperations.PullCurrentBranch();
            }
        }

        private static void MergeBranch(string localBranch, string mergeSource)
        {
            if (!GitOperations.IsBranchBehind(localBranch, mergeSource))
            {
                OldLogger.LogLine(localBranch + " is up to date with " + mergeSource);
                return;
            }

            ProcessHelper failureProc = null;
            if (!GitOperations.SwitchBranch(localBranch, out failureProc))
            {
                OldLogger.LogLine("Unable to switch branches", OldLogger.LevelValue.Warning);
                OldLogger.LogLine(failureProc.AllOutput, OldLogger.LevelValue.Normal);
                return;
            }

            GitOperations.MergeFromBranch(mergeSource);
        }

        private static bool BranchCheck(string localBranch, string remoteBranch)
        {
            if (!GitOperations.IsBranchBehind(localBranch, "origin/" + localBranch))
            {
                OldLogger.LogLine(localBranch + " is up to date with origin/" + localBranch);
                return false;
            }

            ProcessHelper failureProc = null;
            if (!GitOperations.SwitchBranch(localBranch, out failureProc))
            {
                OldLogger.LogLine("Unable to switch branches", OldLogger.LevelValue.Warning);
                OldLogger.LogLine(failureProc.AllOutput, OldLogger.LevelValue.Normal);
                return false;
            }

            return true;
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
