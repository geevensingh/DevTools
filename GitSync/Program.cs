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
            Logger.Level = Logger.LevelValue.SuperChatty;
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

            Logger.Log("Fetching... ");
            GitOperations.FetchAll();
            Logger.LogLine("done");

            Logger.LogLine(@"Examining local branches...");

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
                    Logger.LogLine("Remote branch is gone for " + branch, Logger.LevelValue.Warning);
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
                    Logger.LogLine(@"missing based on: " + branch, Logger.LevelValue.Verbose);
                    missingBasedOn.Add(branch);
                }
            }

            for (int ii = 0; ii < missingBasedOn.Count; ii++)
            {
                string branch = missingBasedOn[ii];
                if (GitOperations.IsReleaseBranchName(branch))
                {
                    missingBasedOn.RemoveAt(ii--);
                    Logger.LogLine(@"not worried about release branch: " + branch, Logger.LevelValue.Verbose);
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
                        Logger.LogLine("Ignoring " + branch, Logger.LevelValue.Warning);
                        Logger.LogLine("Unable to determine the parent branch.  If this is based on " + masterBranch + ", run the following command:");
                        Logger.LogLine("\tgit config branch." + branch + ".basedon " + masterBranch);
                        branchBasedOn.Remove(branch);
                    }
                    else
                    {
                        Logger.LogLine(branch + @" seems to be based on " + basedOnBranch);
                        Logger.LogLine(@"If so, run the following command for faster runs in the future:");
                        Logger.LogLine("\tgit config branch." + branch + ".basedon " + basedOnBranch);
                        Debug.Assert(string.IsNullOrEmpty(branchBasedOn[branch]));
                        branchBasedOn[branch] = basedOnBranch;
                        missingBasedOn.Remove(branch);
                    }
                }
            }

            GitStatus originalStatus = GitOperations.GetStatus();
            String originalBranch = originalStatus.Branch;
            Logger.LogLine("\r\nStarted in " + originalBranch);
            if (originalStatus.AnyChanges)
            {
                if (!GitOperations.Stash())
                {
                    Logger.LogLine("Unable to stash the current work.  Cannot continue.", Logger.LevelValue.Error);
                    Logger.FlushLogs();
                    return (int)Logger.WarningCount;
                }
            }

            ProcessHelper failureProc = null;
            foreach (string branch in localBranchesWithoutRemote)
            {
                if (GitOperations.GetCurrentBranchName() == branch)
                {
                    if (!GitOperations.SwitchBranch(masterBranch, out failureProc))
                    {
                        Logger.LogLine("Unable to switch branches", Logger.LevelValue.Warning);
                        Logger.LogLine(failureProc.AllOutput, Logger.LevelValue.Normal);
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
                Logger.LogLine(string.Empty);

                if (localBranchesWithoutRemote.Contains(branch))
                {
                    if (!GitOperations.SwitchBranch(masterBranch, out failureProc))
                    {
                        Logger.LogLine("Unable to switch branches", Logger.LevelValue.Warning);
                        Logger.LogLine(failureProc.AllOutput, Logger.LevelValue.Normal);
                        continue;
                    }

                    if (GitOperations.DeleteBranch(branch, force: forceDelete) && (branch == originalBranch))
                    {
                        originalBranch = masterBranch;
                    }

                    continue;
                }

                PullBranch(branch);

                string parentBranch = branchBasedOn[branch];
                if (!string.IsNullOrEmpty(parentBranch) && !localBranches.Contains(parentBranch))
                {
                    parentBranch = "origin/" + parentBranch;
                }
                MergeBranch(branch, parentBranch);
            }
            Logger.LogLine(string.Empty);

            if (!GitOperations.SwitchBranch(originalBranch, out failureProc))
            {
                Logger.LogLine("Unable to switch branches", Logger.LevelValue.Error);
                Logger.LogLine(failureProc.AllOutput, Logger.LevelValue.Warning);
                Logger.FlushLogs();
                return (int)Logger.WarningCount;
            }

            if (originalStatus.AnyChanges)
            {
                GitOperations.StashPop();
            }

            Logger.FlushLogs();
            return (int)Logger.WarningCount;
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
                Logger.LogLine(localBranch + " is up to date with " + mergeSource);
                return;
            }

            ProcessHelper failureProc = null;
            if (!GitOperations.SwitchBranch(localBranch, out failureProc))
            {
                Logger.LogLine("Unable to switch branches", Logger.LevelValue.Warning);
                Logger.LogLine(failureProc.AllOutput, Logger.LevelValue.Normal);
                return;
            }

            GitOperations.MergeFromBranch(mergeSource);
        }

        private static bool BranchCheck(string localBranch, string remoteBranch)
        {
            if (!GitOperations.IsBranchBehind(localBranch, "origin/" + localBranch))
            {
                Logger.LogLine(localBranch + " is up to date with origin/" + localBranch);
                return false;
            }

            ProcessHelper failureProc = null;
            if (!GitOperations.SwitchBranch(localBranch, out failureProc))
            {
                Logger.LogLine("Unable to switch branches", Logger.LevelValue.Warning);
                Logger.LogLine(failureProc.AllOutput, Logger.LevelValue.Normal);
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
