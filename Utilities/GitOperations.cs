using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Utilities
{
    public class GitOperations
    {
        public static String GetCurrentBranchName()
        {
            ProcessHelper proc = new ProcessHelper("git.exe", "rev-parse --abbrev-ref HEAD");
            foreach (string line in proc.Go())
            {
                return line.Trim();
            }
            return "Unknown";
        }

        public static GitStatus GetStatus()
        {
            return GitStatus.Get();
        }

        public static bool SwitchBranch(string newBranch)
        {
            ProcessHelper proc = null;
            return SwitchBranch(newBranch, out proc);
        }

        public static bool SwitchBranch(string newBranch, out ProcessHelper proc)
        {
            Logger.LogLine("Switching to " + newBranch);
            proc = new ProcessHelper("git.exe", "checkout " + newBranch);
            proc.Go();
            return proc.ExitCode == 0;
        }

        public static string[] GetLocalBranches()
        {
            return GetLocalBranches(string.Empty);
        }

        public static string[] GetLocalBranches(string args)
        {
            List<string> branches = new List<string>();
            ProcessHelper proc = new ProcessHelper("git.exe", "branch --list --no-column " + args);
            foreach (string line in proc.Go())
            {
                branches.Add(line.Trim(new char[] { ' ', '*' }));
            }
            return branches.ToArray();
        }

        public static string[] GetRemoteBranches()
        {
            return GetRemoteBranches(string.Empty);
        }

        public static string[] GetRemoteBranches(string args)
        {
            return GetLocalBranches(String.Join(" ", new string[] { "--remote", args }));
        }

        public static string[] GetFirstChanges(string[] branches)
        {
            Dictionary<string, string> commits= new Dictionary<string, string>();
            foreach(string branch in branches)
            {
                commits.Add(branch, (new ProcessHelper("git.exe", "merge-base origin/master " + branch)).Go()[0]);
                Logger.LogLine(branch + " seems to have forked from master at " + commits[branch], Logger.LevelValue.Verbose);
            }
            List<string> firstChanges = new List<string>();
            foreach(string branch in commits.Keys)
            {
                string firstChange = GetNextCommitInBranch(commits[branch], branch);
                if (string.IsNullOrEmpty(firstChange))
                {
                    Logger.LogLine("Unable to find any commits in " + branch, Logger.LevelValue.Warning);
                }
                else
                {
                    firstChanges.Add(firstChange);
                    Logger.LogLine("The first commit in " + branch + " seems to be " + firstChange, Logger.LevelValue.Verbose);
                }
            }
            return firstChanges.ToArray();
        }

        public static void FetchAll()
        {
            (new ProcessHelper("git.exe", "fetch --all --tags --prune --quiet")).Go();
        }

        private static string GetNextCommitInBranch(string commit, string branch)
        {
            // %H is full hash
            ProcessHelper proc = new ProcessHelper("git.exe", "log --pretty=format:%H " + commit + ".." + branch);
            string nextCommit = string.Empty;
            foreach (string line in proc.Go())
            {
                nextCommit = line.Trim();
            }

            if (string.IsNullOrEmpty(nextCommit))
            {
                return string.Empty;
            }

            return nextCommit;
        }

        public static string[] GetReleaseBranchNames()
        {
            List<string> releaseBranches = new List<string>();
            ProcessHelper proc = new ProcessHelper("git.exe", "branch -r");
            foreach (string line in proc.Go())
            {
                if (line.Trim().StartsWith("origin/release/"))
                {
                    releaseBranches.Add(line.Trim());
                }
            }
            Logger.LogLine("Found the following release branches:", Logger.LevelValue.Verbose);
            foreach(string branch in releaseBranches)
            {
                Logger.LogLine("\t" + branch, Logger.LevelValue.Verbose);
            }
            return releaseBranches.ToArray();
        }

        public static bool CreateBranch(string branchName, string basedOn)
        {
            return CreateBranch(branchName, basedOn, branchName);
        }

        public static bool CreateBranch(string branchName, string basedOn, string remoteBranchName)
        {
            ProcessHelper proc = new ProcessHelper("git.exe", "checkout -b " + branchName + " " + basedOn);
            proc.Go();
            if (proc.ExitCode != 0)
            {
                return false;
            }
            proc = new ProcessHelper("git.exe", "push -u origin " + remoteBranchName);
            proc.Go();
            return proc.ExitCode == 0;
        }

        public static string BranchContains(string branchName, string[] refs)
        {
            foreach (string refHash in refs)
            {
                ProcessHelper proc = new ProcessHelper("git.exe", "branch --contains " + refHash);
                foreach (string line in proc.Go())
                {
                    if (line.Substring(2).Trim() == branchName)
                    {
                        return refHash;
                    }
                }
            }
            return string.Empty;
        }

        public static void DeleteBranch(string branchName, bool force = false)
        {
            Debug.Assert(GetStatus().Branch != branchName);
            Logger.LogLine((force ? "FORCE " : "") + "Deleting " + branchName, (force ? Logger.LevelValue.Warning : Logger.LevelValue.Normal));
            string deleteArgs = (force ? "-D" : "-d") + " " + branchName;
            ProcessHelper proc = new ProcessHelper("git.exe", "branch " + deleteArgs);
            proc.Go();
            if (proc.ExitCode != 0)
            {
                Logger.LogLine("Unable to delete " + branchName, Logger.LevelValue.Warning);
            }
        }

        public static void MergeFromBranch(string sourceBranch)
        {
            Logger.LogLine("Merging from " + sourceBranch + " to " + GetCurrentBranchName());
            ProcessHelper proc = new ProcessHelper("git.exe", "merge --strategy recursive --strategy-option patience " + sourceBranch);
            string[] lines = proc.Go();
            if (StringHelper.AnyLineContains(lines, "Automatic merge failed"))
            {
                Logger.LogLine("Unable to automatically merge " + GetCurrentBranchName(), Logger.LevelValue.Warning);

                foreach (string line in lines)
                {
                    if (line.StartsWith("CONFLICT"))
                    {
                        Logger.LogLine(line);
                    }
                }

                Logger.LogLine("Aborting merge");
                Logger.LogLine("\tTo attempt again, go to " + GetCurrentBranchName() + " and run:");
                Logger.LogLine("\t\t" + proc.CommandLine);
                (new ProcessHelper("git.exe", "merge --abort")).Go();
            }
        }

        public static void PullCurrentBranch()
        {
            Debug.Assert(!GetStatus().AnyChanges);
            Logger.LogLine("Pulling into " + GetCurrentBranchName());
            (new ProcessHelper("git.exe", "pull")).Go();
        }

        public static bool Stash()
        {
            DateTime now = DateTime.Now;
            Logger.LogLine("Stashing current work");
            string stashMessage = "Automated stash at " + now.ToLongTimeString() + " on " + now.ToShortDateString();
            ProcessHelper proc = new ProcessHelper("git.exe", "stash save -u \"" + stashMessage + "\"");
            proc.Go();
            return proc.StandardError.Length == 0;
        }

        public static void StashPop()
        {
            Debug.Assert(!GetStatus().AnyChanges);
            Logger.LogLine("Restoring work from the stash");
            (new ProcessHelper("git.exe", "stash pop")).Go();
        }

    }
}
