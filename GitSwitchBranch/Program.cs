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
            Logger.AnnounceStartStopActions = true;
            Logger.Level = Logger.LevelValue.Verbose;
#endif

            string currentBranch = GitOperations.GetCurrentBranchName();
            List<string> branches = new List<string>(GitOperations.GetLocalBranches());
            branches.Remove(currentBranch);

            Logger.LogLine(@"Currently on " + currentBranch);
            Logger.LogLine(@"Select from the following:");
            for (int ii = 0; ii < Math.Min(9, branches.Count); ii++)
            {
                Logger.LogLine("\t" + (ii + 1) + " : " + branches[ii]);
            }
            Logger.LogLine("\tn : Make a new branch");
            Logger.LogLine("\tf : Follow an existing remote branch");
            string input = Console.ReadKey().KeyChar.ToString().Trim().ToLower();
            Logger.LogLine(string.Empty);
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
                Logger.LogLine("Branch is now : " + GitOperations.GetCurrentBranchName());
                return;
            }

            int index = -1;
            if (!int.TryParse(input, out index))
            {
                Logger.LogLine(@"Not a valid number: " + input, Logger.LevelValue.Error);
                return;
            }

            if ((index <= 0) || (index > branches.Count))
            {
                Logger.LogLine(@"Invalid index: " + index, Logger.LevelValue.Error);
                return;
            }

            SwitchBranch(branches[index - 1]);
        }

        private static void SwitchBranch(string newBranch)
        {
            ProcessHelper proc = null;
            if (!GitOperations.SwitchBranch(newBranch, out proc))
            {
                Logger.LogLine("Unable to switch branches", Logger.LevelValue.Error);
                Logger.LogLine(proc.AllOutput, Logger.LevelValue.Warning);
            }
        }

        private static void FollowExistingRemoteBranch()
        {
            string searchTerm = Environment.GetEnvironmentVariable("USERNAME").ToLower();
#if DEBUG
            searchTerm = "personal";
#endif
            string[] localBranches = StringHelper.ToLower(GitOperations.GetLocalBranches());
            List<string> remoteBranches = new List<string>(GitOperations.GetRemoteBranches("--sort committerdate"));
            for (int ii = 0; ii < remoteBranches.Count; ii++)
            {
                string lowerRemoteBranch = StringHelper.TrimStart(remoteBranches[ii].ToLower(), "origin/");
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
                Logger.LogLine(@"Select from the following:");
                for (int ii = 0; ii < matchingBranches.Count; ii++)
                {
                    Logger.LogLine("\t" + (ii + 1) + " : " + matchingBranches[ii]);
                }
                Logger.Log("Or");
            }
            Logger.LogLine("Enter another branch name");
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

            if (string.IsNullOrEmpty(basedOn) && remoteBranches.Contains("origin/" + prompt))
            {
                basedOn = "origin/" + prompt;
            }

            if (string.IsNullOrEmpty(basedOn))
            {
                int index = -1;
                if (int.TryParse(prompt, out index) && (index > 0) && (index <= matchingBranches.Count))
                {
                    basedOn = matchingBranches[index - 1];
                }
            }

            if (string.IsNullOrEmpty(basedOn))
            {
                Logger.LogLine("Unable to find the given branch: " + prompt, Logger.LevelValue.Error);
                return;
            }

            CreateBranch(StringHelper.TrimStart(basedOn, "origin/"), basedOn);
        }

        private static void MakeNewBranch()
        {
            string suggestedPrefix = "u/" + Environment.GetEnvironmentVariable("USERNAME") + "/";
            Logger.Log("\r\nPrefix? [" + suggestedPrefix + "] : ");
            string prefix = Console.ReadLine().Trim().ToLower().Replace('_', '-').Replace(' ', '-');
            if (string.IsNullOrEmpty(prefix))
            {
                prefix = suggestedPrefix;
            }
            Logger.Log("Short name : ");
            string shortName = Console.ReadLine().Trim().ToLower().Replace('_', '-').Replace(' ', '-');
            if (string.IsNullOrEmpty(shortName))
            {
                Logger.LogLine("Short name must be provided!", Logger.LevelValue.Error);
                return;
            }
            string branchName = string.Join("/", string.Join("/", new string[] { prefix, shortName }).Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries));

            string suggestedBasedOn = "master";
            Logger.Log("Based on what branch? [" + suggestedBasedOn + "] : ");
            string basedOn = Console.ReadLine().Trim();
            if (string.IsNullOrEmpty(basedOn))
            {
                basedOn = suggestedBasedOn;
            }

            string remoteBranchName = "origin/" + branchName;
            Logger.LogLine("Confirming new branch called " + branchName + " based on " + basedOn);
            Logger.LogLine("This will also be tracking " + remoteBranchName);
            Logger.Log("That look ok? [y] ");
            string prompt = Console.ReadKey().KeyChar.ToString().Trim().ToLower();
            Logger.LogLine(string.Empty);
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
            if (!GitOperations.CreateBranch(branchName, basedOn))
            {
                Logger.LogLine("Unable to create your branch", Logger.LevelValue.Error);
            }
        }
    }
}
