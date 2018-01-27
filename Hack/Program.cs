using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Utilities;

namespace Hack
{
    class Program
    {
        struct Counts
        {
            public int files;
            public int inserts;
            public int deletes;
            public string commit;

            public Counts(string commitId, int fileCount, int insertCount, int deleteCount)
            {
                commit = commitId;
                files = fileCount;
                inserts = insertCount;
                deletes = deleteCount;
            }
        }

        static int Main(string[] args)
        {
            Dictionary<string, List<Counts>> dict = new Dictionary<string, List<Counts>>();
            ProcessHelper proc = new ProcessHelper("git.exe", "log -n 1200 --pretty=\"%H %ae %ad\"");
            int maxAuthorLength = 0;
            foreach (string line in proc.Go())
            {
                string[] splits = line.Split(new char[] { ' ' });

                string commitId = splits[0];
                string author = StringHelper.TrimEnd(splits[1], "@microsoft.com");
                maxAuthorLength = Math.Max(maxAuthorLength, author.Length);

                int fileCount, insertCount, deleteCount;
                GitOperations.GetCommitStats(commitId, out fileCount, out insertCount, out deleteCount);
                if (!dict.ContainsKey(author))
                {
                    dict[author] = new List<Counts>();
                }
                dict[author].Add(new Counts(commitId, fileCount, insertCount, deleteCount));
                Logger.Log(".");
            }
            Logger.LogLine(" done");

            foreach (string author in dict.Keys)
            {
                List<Counts> list = dict[author];
                int totalFileCount = 0;
                int totalInsertCount = 0;
                int totalDeleteCount = 0;

                foreach(Counts count in list)
                {
                    totalFileCount += count.files;
                    totalInsertCount += count.inserts;
                    totalDeleteCount += count.deletes;
                }

                string fileCountString = PluralStringCheck(totalFileCount, "file changed", "files changed");
                string insertCountString = PluralStringCheck(totalFileCount, "insertion(+)", "insertions(+)");
                string deleteCountString = PluralStringCheck(totalFileCount, "deletion(+)", "deletions(+)");
                string netCountString = PluralStringCheck((totalInsertCount - totalDeleteCount), "net line changed", "net lines changed");
                Logger.LogLine(("Author: " + author).PadRight(maxAuthorLength + 10) + fileCountString.PadRight(20) + insertCountString.PadRight(20) + deleteCountString.PadRight(20) + netCountString);
            }

            foreach (string author in dict.Keys)
            {
                List<Counts> list = dict[author];
                int totalFileCount = 0;
                int totalInsertCount = 0;
                int totalDeleteCount = 0;

                foreach (Counts count in list)
                {
                    totalFileCount += count.files;
                    totalInsertCount += count.inserts;
                    totalDeleteCount += count.deletes;
                }

                Logger.LogLine(author.PadRight(maxAuthorLength + 10) + totalFileCount.ToString().PadRight(20) + totalInsertCount.ToString().PadRight(20) + totalDeleteCount.ToString().PadRight(20));
            }

            foreach (string author in dict.Keys)
            {
                Logger.LogLine("------------------------------------");
                Logger.LogLine("Author: " + author);
                List<Counts> list = dict[author];
                foreach(Counts count in list)
                {
                    Logger.LogLine(count.commit.PadRight(45) + count.files.ToString().PadRight(20) + count.inserts.ToString().PadRight(20) + count.deletes.ToString().PadRight(20));
                }
            }

            return 0;
        }

        static string PluralStringCheck(int count, string singularString, string pluralString)
        {
            if (count > 1)
            {
                return count + " " + pluralString;
            }
            return count + " " + singularString;
        }

        static bool RevertFile(string filePath)
        {
            ProcessHelper proc = new ProcessHelper("git.exe", @"checkout -- " + filePath);
            proc.Go();
            if (proc.ExitCode != 0 || proc.StandardError.Length != 0)
            {
                Logger.LogLine("Unable to revert " + filePath, Logger.LevelValue.Warning);
                return false;
            }
            return true;
        }

        static string[] GetAllFiles(string root, string[] extensions)
        {
            Debug.Assert(Directory.Exists(root));
            List<string> allFiles = new List<string>();
            foreach (string ext in extensions)
            {
                allFiles.AddRange(Directory.EnumerateFiles(root, @"*." + ext, SearchOption.AllDirectories));
            }
            return allFiles.ToArray();
        }
    }
}
