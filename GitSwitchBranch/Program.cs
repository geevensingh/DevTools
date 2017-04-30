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
            string currentBranch = GitOperations.GetCurrentBranchName();
            List<string> branches = new List<string>(GitOperations.GetLocalBranches(" --sort committerdate"));
            branches.Remove(currentBranch);

            Logger.LogLine(@"Currently on " + currentBranch);
            Logger.LogLine(@"Select from the following:");
            for (int ii = 0; ii < Math.Min(9, branches.Count); ii++)
            {
                Logger.LogLine("\t" + (ii + 1) + " : " + branches[ii]);
            }
            string input = Console.ReadKey().KeyChar.ToString();
            if (string.IsNullOrEmpty(input) || (input.ToLower() == "q"))
            {
                return;
            }

            if (input.ToLower() == "n")
            {
                MakeNewBranch();
                return;
            }

            if ((input == "-") || (input == "--"))
            {
                GitOperations.SwitchBranch("-");
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

            GitOperations.SwitchBranch(branches[index - 1]);
            return;
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

            Console.WriteLine("git checkout -b " + branchName + " " + basedOn);
            Console.WriteLine("git branch --set-upstream-to origin/" + branchName);
        }
    }
}
