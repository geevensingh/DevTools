using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utilities;

namespace GitSwitchBranch
{
    class Program
    {
        static void Main(string[] args)
        {
#if DEBUG
            OldLogger.AnnounceStartStopActions = true;
            OldLogger.Level = OldLogger.LevelValue.Verbose;
#endif

            string currentBranch = GitOperations.GetCurrentBranchName();
            List<string> branches = new List<string>(GitOperations.GetLocalBranches());
            branches.Remove(currentBranch);

            OldLogger.LogLine(@"Currently on " + currentBranch);
            OldLogger.LogLine(@"Select from the following:");
            for (int ii = 0; ii < branches.Count; ii++)
            {
                OldLogger.LogLine("\t" + (ii + 1) + " : " + branches[ii]);
            }
            OldLogger.LogLine("\tn : Make a new branch");
            OldLogger.LogLine("\tf : Follow an existing remote branch");
            string input = Console.ReadLine().Trim().ToLower();
            OldLogger.LogLine(string.Empty);
            if (string.IsNullOrEmpty(input) || (input.ToLower() == "q"))
            {
                return;
            }

            if (input.ToLower() == "n")
            {
                MakeNewBranch();
                return;
            }

            if (input.ToLower() == "f")
            {
                FollowExistingRemoteBranch();
                return;
            }

            if ((input == "-") || (input == "--"))
            {
                SwitchBranch("-");
                OldLogger.LogLine("Branch is now : " + GitOperations.GetCurrentBranchName());
                return;
            }

            int index = -1;
            if (!int.TryParse(input, out index))
            {
                OldLogger.LogLine(@"Not a valid number: " + input, OldLogger.LevelValue.Error);
                return;
            }

            if ((index <= 0) || (index > branches.Count))
            {
                OldLogger.LogLine(@"Invalid index: " + index, OldLogger.LevelValue.Error);
                return;
            }

            SwitchBranch(branches[index - 1]);
        }

        private static void SwitchBranch(string newBranch)
        {
            if (!GitOperations.SwitchBranch(newBranch, out ProcessHelper proc))
            {
                OldLogger.LogLine("Unable to switch branches", OldLogger.LevelValue.Error);
                OldLogger.LogLine(proc.AllOutput, OldLogger.LevelValue.Warning);
            }
        }

        private static void FollowExistingRemoteBranch()
        {
            string searchTerm = Environment.GetEnvironmentVariable("USERNAME").ToLower();
#if DEBUG
            searchTerm = "personal";
#endif
            string[] localBranches = StringExtensions.ToLower(GitOperations.GetLocalBranches());
            List<string> remoteBranches = new List<string>(GitOperations.GetRemoteBranches("--sort committerdate"));
            for (int ii = 0; ii < remoteBranches.Count; ii++)
            {
                string lowerRemoteBranch = StringExtensions.TrimStart(remoteBranches[ii].ToLower(), "origin/");
                if (localBranches.Contains(lowerRemoteBranch))
                {
                    remoteBranches.RemoveAt(ii--);
                }
            }

            List<string> matchingBranches = new List<string>();
            foreach(string remoteBranch in remoteBranches)
            {
                if (remoteBranch.ToLower().Contains(searchTerm))
                {
                    matchingBranches.Add(remoteBranch);
                }
            }
            if (matchingBranches.Count > 0)
            {
                OldLogger.LogLine(@"Select from the following:");
                for (int ii = 0; ii < matchingBranches.Count; ii++)
                {
                    OldLogger.LogLine("\t" + (ii + 1) + " : " + matchingBranches[ii]);
                }
                OldLogger.Log("Or");
            }
            OldLogger.LogLine("Enter another branch name");
            string prompt = Console.ReadLine().Trim();
            if (string.IsNullOrEmpty(prompt) || (prompt.ToLower() == "q"))
            {
                return;
            }

            string basedOn = string.Empty;
            if (remoteBranches.Contains(prompt))
            {
                basedOn = prompt;
            }
            else if (remoteBranches.Contains("origin/" + prompt))
            {
                basedOn = "origin/" + prompt;
            }
            else
            {
                int index = -1;
                if (int.TryParse(prompt, out index) && (index > 0) && (index <= matchingBranches.Count))
                {
                    basedOn = matchingBranches[index - 1];
                }
            }

            if (string.IsNullOrEmpty(basedOn))
            {
                OldLogger.LogLine("Unable to find the given branch: " + prompt, OldLogger.LevelValue.Error);
                return;
            }

            CreateBranch(StringExtensions.TrimStart(basedOn, "origin/"), basedOn);
        }

        private static string _defaultBranch = null;
        private static string GetDefaultBranch()
        {
            _defaultBranch = _defaultBranch ?? GitOperations.GetDefaultBranch();
            return _defaultBranch;
        }

        private static void MakeNewBranch()
        {
            string suggestedPrefix = "u/" + Environment.GetEnvironmentVariable("USERNAME") + "/";
            OldLogger.Log("\r\nPrefix? [" + suggestedPrefix + "] : ");
            string prefix = Console.ReadLine().Trim().ToLower().Replace('_', '-').Replace(' ', '-');
            if (string.IsNullOrEmpty(prefix))
            {
                prefix = suggestedPrefix;
            }
            OldLogger.Log("Short name : ");
            string shortName = Console.ReadLine().Trim().ToLower().Replace('_', '-').Replace(' ', '-');
            if (string.IsNullOrEmpty(shortName))
            {
                OldLogger.LogLine("Short name must be provided!", OldLogger.LevelValue.Error);
                return;
            }
            string branchName = string.Join("/", string.Join("/", new string[] { prefix, shortName }).Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries));

            string suggestedBasedOn = GitOperations.GetBranchBase(GitOperations.GetCurrentBranchName());
            if (string.IsNullOrEmpty(suggestedBasedOn))
            {
                suggestedBasedOn = GetDefaultBranch();
            }
            OldLogger.Log("Based on what branch? [" + suggestedBasedOn + "] : ");
            string basedOn = Console.ReadLine().Trim();
            if (string.IsNullOrEmpty(basedOn))
            {
                basedOn = suggestedBasedOn;
            }

            string remoteBranchName = "origin/" + branchName;
            OldLogger.LogLine("Confirming new branch called " + branchName + " based on " + basedOn);
            OldLogger.LogLine("This will also be tracking " + remoteBranchName);
            OldLogger.Log("That look ok? [y] ");
            string prompt = Console.ReadKey().KeyChar.ToString().Trim().ToLower();
            OldLogger.LogLine(string.Empty);
            if (string.IsNullOrEmpty(prompt))
            {
                prompt = "y";
            }
            if (prompt == "y")
            {
                CreateBranch(branchName, basedOn);
            }
        }

        private static void CreateBranch(string branchName, string basedOn)
        {
            if (!GitOperations.CreateBranch(branchName, basedOn, out ProcessHelper proc))
            {
                OldLogger.LogLine("Unable to create your branch", OldLogger.LevelValue.Error);
                OldLogger.LogLine(proc.AllOutput, OldLogger.LevelValue.Warning);
            }
        }
    }
}
