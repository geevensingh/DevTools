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

        public static String GetFileStatus()
        {
            GitStatus status = GitStatus.Get();
            status.WriteToConsole();
            Console.WriteLine();
            return "[ " +
                "+" + status.StagedAdded + " " +
                "~" + status.StagedModified + " " +
                "!" + status.StagedDeleted + " | " +
                "+" + status.UnstagedAdded + " " +
                "~" + status.UnstagedModified + " " +
                "!" + status.UnstagedDeleted + " ]";
        }

        public static void Stash()
        {
            DateTime now = DateTime.Now;
            string stashMessage = "Automated stash at " + now.ToLongTimeString() + " on " + now.ToShortDateString();
            (new ProcessHelper("git.exe", "stash save -u \"" + stashMessage + "\"")).Go();
        }

        public static void StashPop()
        {
            Debug.Assert(!GetStatus().AnyChanges);
            (new ProcessHelper("git.exe", "stash pop")).Go();
        }

    }
}
