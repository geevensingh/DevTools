using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public static String GetFileStatus()
        {
            GitStatus status = GitStatus.GetStatus();
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
    }
}
