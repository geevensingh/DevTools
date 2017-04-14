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

        public static void SwitchBranch(string newBranch)
        {
            Debug.Assert(!GetStatus().AnyChanges);
            Logger.LogLine("Switching to " + newBranch);
            (new ProcessHelper("git.exe", "checkout " + newBranch)).Go();
        }

        public static string[] GetLocalBranches()
        {
            List<string> branches = new List<string>();
            ProcessHelper proc = new ProcessHelper("git.exe", "branch --list --no-column");
            foreach (string line in proc.Go())
            {
                branches.Add(line.Trim(new char[] { ' ', '*' }));
            }
            return branches.ToArray();
        }

        public static string[] GetReleaseForkPoints()
        {
            List<string> forkPoints = new List<string>();
            foreach(string branch in GetReleaseBranchNames())
            {
                forkPoints.Add((new ProcessHelper("git.exe", "merge-base --fork-point " + branch)).Go()[0]);
            }
            return forkPoints.ToArray();
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
            return releaseBranches.ToArray();
        }

        public static bool BranchContains(string branchName, string[] releaseBranchNames)
        {
            foreach (string forkpoint in releaseBranchNames)
            {
                ProcessHelper proc = new ProcessHelper("git.exe", "branch --contains " + forkpoint);
                foreach (string line in proc.Go())
                {
                    if (line.Substring(2).Trim() == branchName)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static void DeleteBranch(string branchName, bool force = false)
        {
            Debug.Assert(GetStatus().Branch != branchName);
            Logger.LogLine((force ? "FORCE " : "") + "Deleting " + branchName, (force ? Logger.LevelValue.Warning : Logger.LevelValue.Normal));
            string deleteArgs = (force ? "-D" : "-d") + " " + branchName;
            (new ProcessHelper("git.exe", "branch " + deleteArgs)).Go();
        }

        public static void MergeFromBranch(string sourceBranch)
        {
            Logger.LogLine("Merging from " + sourceBranch + " to " + GetCurrentBranchName());
            ProcessHelper proc = new ProcessHelper("git.exe", "merge --strategy recursive --strategy-option patience " + sourceBranch);
            bool abort = false;
            foreach (string line in proc.Go())
            {
                if (line.Contains("Automatic merge failed"))
                {
                    abort = true;
                }
            }

            if (abort)
            {
                Logger.LogLine("Unable to automatically merge " + GetCurrentBranchName(), Logger.LevelValue.Warning);
                (new ProcessHelper("git.exe", "merge --abort")).Go();
            }
        }

        public static void PullCurrentBranch()
        {
            Debug.Assert(!GetStatus().AnyChanges);
            Logger.LogLine("Pulling into " + GetCurrentBranchName());
            (new ProcessHelper("git.exe", "pull")).Go();
        }

        public static void Stash()
        {
            DateTime now = DateTime.Now;
            Logger.LogLine("Stashing current work");
            string stashMessage = "Automated stash at " + now.ToLongTimeString() + " on " + now.ToShortDateString();
            (new ProcessHelper("git.exe", "stash save -u \"" + stashMessage + "\"")).Go();
        }

        public static void StashPop()
        {
            Debug.Assert(!GetStatus().AnyChanges);
            Logger.LogLine("Restoring work from the stash");
            (new ProcessHelper("git.exe", "stash pop")).Go();
        }

    }
}
