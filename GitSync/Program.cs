namespace GitSync
{
    using System;
    using System.Collections.Generic;
    using Utilities;
    using System.Diagnostics;
    using System.Linq;

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

            List<BranchInfo> localBranches = GitOperations.GetLocalBranches().Select(x => new BranchInfo(x)).ToList();
            string defaultBranchName = GitOperations.GetDefaultBranch();
            BranchInfo localDefaultBranch = localBranches.FirstOrDefault(x => x.Name == defaultBranchName);
            if (localDefaultBranch == null)
            {
                defaultBranchName = "origin/" + defaultBranchName;
            }
            else
            {
                localDefaultBranch.IsDefault = true;
            }

            List<string> remoteBranches = new List<string>(GitOperations.GetRemoteBranches());
            foreach (BranchInfo branch in localBranches)
            {
                branch.HasRemoteBranch = remoteBranches.Contains(branch.Name) || remoteBranches.Contains("origin/" + branch.Name) || branch.Name.Contains("HEAD");
                if (!branch.HasRemoteBranch)
                {
                    OldLogger.LogLine($"Remote branch is gone for {branch.Name}", OldLogger.LevelValue.Warning);
                }
            }

            List<BranchInfo> unparentedBranches = localBranches.Where(x => !x.IsParented && !x.IsDefault).ToList();
            if (unparentedBranches.Count > 0)
            {
                foreach (BranchInfo branch in unparentedBranches)
                {
                    OldLogger.LogLine($"{branch.Name} has no parent branch");
                }

                string exampleBranchName = "<branch name>";
                if (unparentedBranches.Count == 1)
                {
                    exampleBranchName = unparentedBranches.First().Name;
                }
                OldLogger.LogLine($"To set {defaultBranchName} as the parent branch, run the following command:");
                OldLogger.LogLine($"\tgit config branch.{exampleBranchName}.basedon {defaultBranchName}");
            }

            GitStatus originalStatus = GitOperations.GetStatus();
            string originalBranchName = originalStatus.Branch;
            OldLogger.LogLine("\r\nStarted in " + originalBranchName);
            if (originalStatus.AnyChanges)
            {
                if (!GitOperations.Stash())
                {
                    OldLogger.LogLine("Unable to stash the current work.  Cannot continue.", OldLogger.LevelValue.Error);
                    OldLogger.FlushLogs();
                    return (int)OldLogger.WarningCount;
                }
            }

            //
            // Delete local branches that don't have a remote branch
            //
            ProcessHelper failureProc = null;
            foreach (BranchInfo branch in localBranches.Where(x => !x.HasRemoteBranch && !x.IsDefault))
            {
                if (GitOperations.GetCurrentBranchName() == branch.Name)
                {
                    if (!GitOperations.SwitchBranch(defaultBranchName, out failureProc))
                    {
                        OldLogger.LogLine("Unable to switch branches", OldLogger.LevelValue.Warning);
                        OldLogger.LogLine(failureProc.AllOutput, OldLogger.LevelValue.Normal);
                        continue;
                    }
                }

                if (GitOperations.DeleteBranch(branch.Name, force: forceDelete))
                {
                    if (branch.Name == originalBranchName)
                    {
                        originalBranchName = defaultBranchName;
                    }
                    branch.IsDeleted = true;
                }
            }
            localBranches = localBranches.Where(x => !x.IsDeleted).ToList();

            foreach (BranchInfo branch in SortParentBranchesFirst(localBranches))
            {
                OldLogger.LogLine(string.Empty);

                UpdateBranch(branch.Name);

                if (branch.IsParented)
                {
                    MergeBranch(branch.Name, branch.ParentBranchName);
                }
            }
            OldLogger.LogLine(string.Empty);

            if (!GitOperations.SwitchBranch(originalBranchName, out failureProc))
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

        private static IEnumerable<BranchInfo> SortParentBranchesFirst(IEnumerable<BranchInfo> localBranches)
        {
            List<BranchInfo> branches = localBranches.ToList();
            for (int ii = 0; ii < branches.Count; ii++)
            {
                BranchInfo branch = branches[ii];
                if (branch.IsParented)
                {
                    int parentIndex = branches.FindIndex(x => x.Name == branch.ParentBranchName);
                    if (parentIndex > ii)
                    {
                        branches[ii] = branches[parentIndex];
                        branches[parentIndex] = branch;
                        return SortParentBranchesFirst(branches);
                    }
                }
            }
            return localBranches;
        }

        private static void MergeBranch(string localBranch, string mergeSource)
        {
            if (!GitOperations.IsBranchBehind(localBranch, mergeSource))
            {
                OldLogger.LogLine(localBranch + " is up to date with " + mergeSource);
                return;
            }

            if (!GitOperations.SwitchBranch(localBranch, out ProcessHelper failureProc))
            {
                OldLogger.LogLine("Unable to switch branches", OldLogger.LevelValue.Warning);
                OldLogger.LogLine(failureProc.AllOutput, OldLogger.LevelValue.Normal);
                return;
            }

            GitOperations.MergeFromBranch(mergeSource);
        }

        private static bool UpdateBranch(string localBranch, string remoteBranch = null)
        {
            remoteBranch = remoteBranch ?? ("origin/" + localBranch);

            if (!GitOperations.IsBranchBehind(localBranch, remoteBranch))
            {
                OldLogger.LogLine($"{localBranch} is up to date with {remoteBranch}");
                return false;
            }

            if (!GitOperations.SwitchBranch(localBranch, out ProcessHelper failureProc))
            {
                OldLogger.LogLine("Unable to switch branches", OldLogger.LevelValue.Warning);
                OldLogger.LogLine(failureProc.AllOutput, OldLogger.LevelValue.Normal);
                return false;
            }

            GitOperations.PullCurrentBranch();

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
