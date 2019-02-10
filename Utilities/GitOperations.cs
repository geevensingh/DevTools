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
            OldLogger.LogLine("Switching to " + newBranch);
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

        public static Dictionary<string, string> GetUniqueCommits(string masterBranch, string[] branches)
        {
            if (!masterBranch.StartsWith("origin/"))
            {
                masterBranch = "origin/" + masterBranch;
            }
            Dictionary<string, string> forkpoints = new Dictionary<string, string>();
            foreach (string branch in branches)
            {
                string forkPoint = (new ProcessHelper("git.exe", "merge-base " + masterBranch + " " + branch)).Go()[0];
                Debug.Assert(forkPoint.Length == 40);
                string trimmedBranch = StringHelper.TrimStart(branch, @"origin/");
                forkpoints[trimmedBranch] = forkPoint;
                OldLogger.LogLine(trimmedBranch + " seems to have forked from " + masterBranch + " at " + forkpoints[trimmedBranch], OldLogger.LevelValue.Verbose);
            }
            Dictionary<string, string> uniqueCommits = new Dictionary<string, string>();
            foreach (string branch in forkpoints.Keys)
            {
                string firstChange = GetNextCommitInBranch(forkpoints[branch], @"origin/" + branch);
                if (string.IsNullOrEmpty(firstChange))
                {
                    OldLogger.LogLine("Unable to find any commits in " + branch, OldLogger.LevelValue.Warning);
                }
                else
                {
                    uniqueCommits[branch] = firstChange;
                    OldLogger.LogLine("The first commit in " + branch + " seems to be " + firstChange, OldLogger.LevelValue.Verbose);
                }
            }
            return uniqueCommits;
        }

        public static string GetBranchBase(string branch)
        {
            ProcessHelper proc = new ProcessHelper("git.exe", "config branch." + branch + ".basedon");
            proc.Go();
            if (proc.ExitCode != 0)
            {
                return string.Empty;
            }
            Debug.Assert(proc.StandardOutput.Length > 0);
            return proc.StandardOutput[0];
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

        public static bool IsReleaseBranchName(string branchName)
        {
            branchName = StringHelper.TrimStart(branchName.Trim(), "origin/");
            return branchName.StartsWith("release/");
        }

        public static string[] GetReleaseBranchNames()
        {
            List<string> releaseBranches = new List<string>();
            ProcessHelper proc = new ProcessHelper("git.exe", "branch -r");
            foreach (string line in proc.Go(OldLogger.LevelValue.SuperChatty))
            {
                if (IsReleaseBranchName(line))
                {
                    releaseBranches.Add(line.Trim());
                }
            }
            OldLogger.LogLine("Found the following release branches:", OldLogger.LevelValue.SuperChatty);
            foreach (string branch in releaseBranches)
            {
                OldLogger.LogLine("\t" + branch, OldLogger.LevelValue.SuperChatty);
            }
            return releaseBranches.ToArray();
        }

        public static bool CreateBranch(string branchName, string basedOn, out ProcessHelper proc)
        {
            return CreateBranch(branchName, basedOn, branchName, out proc);
        }

        public static bool CreateBranch(string branchName, string basedOn)
        {
            ProcessHelper proc = null;
            return CreateBranch(branchName, basedOn, branchName, out proc);
        }

        public static bool CreateBranch(string branchName, string basedOn, string remoteBranchName, out ProcessHelper proc)
        {
            proc = new ProcessHelper("git.exe", "checkout -b " + branchName + " " + basedOn);
            proc.Go();
            if (proc.ExitCode != 0)
            {
                return false;
            }

            proc = new ProcessHelper("git.exe", "push -u origin " + remoteBranchName);
            proc.Go();
            if (proc.ExitCode != 0)
            {
                return false;
            }

            if (branchName != basedOn && basedOn != ("origin/" + branchName))
            {
                proc = new ProcessHelper("git.exe", string.Join(" ", new string[] { "config", "branch." + branchName + ".basedon", basedOn }));
                proc.Go();
            }
            return proc.ExitCode == 0;
        }

        public static string BranchContains(string branchName, Dictionary<string, string> uniqueCommits)
        {
            foreach(string otherBranch in uniqueCommits.Keys)
            {
                string refHash = uniqueCommits[otherBranch];
                ProcessHelper proc = new ProcessHelper("git.exe", "branch --contains " + refHash);
                foreach (string line in proc.Go())
                {
                    if (line.Substring(2).Trim() == branchName)
                    {
                        return otherBranch;
                    }
                }
            }
            return string.Empty;
        }

        public static bool DeleteBranch(string branchName, bool force = false)
        {
            Debug.Assert(GetStatus().Branch != branchName);
            OldLogger.LogLine((force ? "FORCE " : "") + "Deleting " + branchName, (force ? OldLogger.LevelValue.Warning : OldLogger.LevelValue.Normal));
            string deleteArgs = (force ? "-D" : "-d") + " " + branchName;
            ProcessHelper proc = new ProcessHelper("git.exe", "branch " + deleteArgs);
            proc.Go();
            bool success = (proc.ExitCode == 0);
            if (!success)
            {
                OldLogger.LogLine("Unable to delete " + branchName, OldLogger.LevelValue.Warning);
            }
            return success;
        }

        public static void MergeFromBranch(string sourceBranch)
        {
            OldLogger.LogLine("Merging from " + sourceBranch + " to " + GetCurrentBranchName());
            ProcessHelper proc = new ProcessHelper("git.exe", "merge --strategy recursive --strategy-option patience " + sourceBranch);
            proc.Go();
            string[] lines = proc.AllOutput;
            if (StringHelper.AnyLineContains(lines, "Automatic merge failed"))
            {
                OldLogger.LogLine("Unable to automatically merge " + GetCurrentBranchName(), OldLogger.LevelValue.Warning);

                foreach (string line in lines)
                {
                    if (line.StartsWith("CONFLICT"))
                    {
                        OldLogger.LogLine(line);
                    }
                }

                OldLogger.LogLine("Aborting merge");
                OldLogger.LogLine("\tTo attempt again, go to " + GetCurrentBranchName() + " and run:");
                OldLogger.LogLine("\t\t" + proc.CommandLine);
                (new ProcessHelper("git.exe", "merge --abort")).Go();
            }
        }

        public static bool IsBranchBehind(string branchName, string remoteBranchName)
        {
            if (string.IsNullOrEmpty(remoteBranchName ))
            {
                remoteBranchName = "origin/" + branchName;
            }

            // git log --pretty=format:%H origin/master --not master
            ProcessHelper proc = new ProcessHelper("git.exe", "log --pretty=format:%H " + remoteBranchName + " --not " + branchName);
            string nextCommit = string.Empty;
            foreach (string line in proc.Go())
            {
                Debug.Assert(!line.StartsWith("fatal"));
                if (!string.IsNullOrEmpty(line.Trim()))
                {
                    return true;
                }
            }
            return false;
        }

        public static void PullCurrentBranch()
        {
            Debug.Assert(!GetStatus().AnyChanges);
            OldLogger.LogLine("Pulling into " + GetCurrentBranchName());
            (new ProcessHelper("git.exe", "pull")).Go();
        }

        public static bool Stash()
        {
            DateTime now = DateTime.Now;
            OldLogger.LogLine("Stashing current work");
            string stashMessage = "Automated stash at " + now.ToLongTimeString() + " on " + now.ToShortDateString();
            ProcessHelper proc = new ProcessHelper("git.exe", "stash save --include-untracked \"" + stashMessage + "\"");
            proc.Go();
            bool hasError = proc.StandardError.Length != 0;
            if (hasError)
            {
                OldLogger.LogLine(proc.AllOutput, OldLogger.LevelValue.Warning);
            }
            return !hasError;
        }

        public static void StashPop()
        {
            Debug.Assert(!GetStatus().AnyChanges);
            OldLogger.LogLine("Restoring work from the stash");
            (new ProcessHelper("git.exe", "stash pop")).Go();
        }

        public static void GetCommitStats(string commitId, out int fileCount, out int insertCount, out int deleteCount)
        {
            fileCount = 0;
            insertCount = 0;
            deleteCount = 0;

            // git show fe324ede049abc34592d73fbe53f6aeb4c146a24 --pretty="" --numstat -w
            ProcessHelper proc = new ProcessHelper("git.exe", "show " + commitId + " --pretty=\"\" --numstat -w --word-diff=porcelain");
            foreach (string line in proc.Go())
            {
                string[] splits = line.Split(new char[] { '\t' });
                Debug.Assert(splits.Length == 3);
                if (StringHelper.EndsWithAny(splits[2], new string[] { "resx", "resw" }))
                {
                    continue;
                }

                if (splits[2].StartsWith("data/") || splits[2].StartsWith("tools/") || splits[2].StartsWith("Generated/"))
                {
                    continue;
                }

                if (splits[0] == "-")
                {
                    Debug.Assert(splits[1] == "-");
                    continue;
                }

                fileCount++;
                insertCount += int.Parse(splits[0]);
                deleteCount += int.Parse(splits[1]);
            }
        }
    }
}
