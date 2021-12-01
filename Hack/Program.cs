using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Utilities;
using Newtonsoft.Json;
using FluentAssertions;
using Logging;

namespace Hack
{
    static class Program
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

        private static string ErrorMessage = string.Empty;

        [LogMethod(LogLevel.Error)]
        static private bool TryParseAccountIdAndScheduleId(ref string accountId, ref string scheduleId)
        {
            char[] delimiters = new char[] { '/', ',', '\t', ' ', '\r', '\n' };
            accountId = accountId.Trim(delimiters);
            scheduleId = scheduleId.Trim(delimiters);

            if (string.IsNullOrWhiteSpace(accountId))
            {
                ErrorMessage = "AccountId is empty - valid accountId required.";
                return false;
            }


            if (accountId.IndexOfAny(delimiters) != -1)
            {
                if (!string.IsNullOrWhiteSpace(scheduleId))
                {
                    ErrorMessage = "If all data is given in accountId field, then scheduleId must be empty.";
                    return false;
                }

                List<string> parts = new List<string>(accountId.Split(delimiters, StringSplitOptions.RemoveEmptyEntries));
                parts.Remove("capture-schedules");
                if (parts.Count != 2)
                {
                    ErrorMessage = string.Format("Unable to parse accountId: {0}", accountId);
                    return false;
                }

                accountId = parts[0];
                scheduleId = parts[1];
            }

            if (string.IsNullOrWhiteSpace(accountId) ||
                !Guid.TryParse(accountId.Trim(), out _))
            {
                ErrorMessage = string.Format("{0} is an invalid accountId, Valid accountId required.", accountId);
                return false;
            }

            if (string.IsNullOrWhiteSpace(scheduleId) ||
                !Guid.TryParse(scheduleId.Trim(), out _))
            {
                ErrorMessage = string.Format("{0} is an invalid scheduleId, Valid scheduleId required.", scheduleId);
                return false;
            }

            return true;
        }

        [LogMethod]
        static private void Test(string accountId, string scheduleId, bool expectedResult)
        {
            Logger.Instance.LogLine("input-accountId:    " + accountId);
            Logger.Instance.LogLine("input-scheduleId:   " + scheduleId);
            bool result = TryParseAccountIdAndScheduleId(ref accountId, ref scheduleId);
            Logger.Instance.LogLine("result:             " + result.ToString());
            Debug.Assert(result == expectedResult);
            Logger.Instance.LogLine("output-accountId:   " + accountId);
            Logger.Instance.LogLine("output-scheduleId:  " + scheduleId);
            if (expectedResult)
            {
                Debug.Assert(Guid.ParseExact(accountId, "D").ToString("D") == accountId);
                Debug.Assert(Guid.ParseExact(scheduleId, "N").ToString("N") == scheduleId);
            }
        }


        static int Main(string[] args)
        {
            ConsoleLogger.Instance.IncludeEventType = false;

            Test("032fa944-399a-4c04-9090-7ce1fd722a0d", "d6d64cae9b4074b5c02f574d12de535f", true);
            Test("   032fa944-399a-4c04-9090-7ce1fd722a0d  ", "   d6d64cae9b4074b5c02f574d12de535f   ", true);
            Test("/032fa944-399a-4c04-9090-7ce1fd722a0d/capture-schedules/d6d64cae9b4074b5c02f574d12de535f", "", true);
            Test("   /032fa944-399a-4c04-9090-7ce1fd722a0d/capture-schedules/d6d64cae9b4074b5c02f574d12de535f/    ", "", true);
            Test("032fa944-399a-4c04-9090-7ce1fd722a0d,d6d64cae9b4074b5c02f574d12de535f", "", true);
            Test("   032fa944-399a-4c04-9090-7ce1fd722a0d  ,   d6d64cae9b4074b5c02f574d12de535f   ", "", true);
            Test("032fa944-399a-4c04-9090-7ce1fd722a0d	d6d64cae9b4074b5c02f574d12de535f", "", true);
            Test("   032fa944-399a-4c04-9090-7ce1fd722a0d  	   d6d64cae9b4074b5c02f574d12de535f   ", "", true);
            Test("032fa944-399a-4c04-9090-7ce1fd722a0d/d6d64cae9b4074b5c02f574d12de535f", "", true);
            Test("   032fa944-399a-4c04-9090-7ce1fd722a0d  /   d6d64cae9b4074b5c02f574d12de535f   ", "", true);
            Test("   032fa944-399a-4c04-9090-7ce1fd722a0d  /   d6d64cae9b4074b5c02f574d12de535f   ", "", true);

            Test("032fa944-399a-4c04-9090-7ce1fd722a0d", "d6d64cae9b40702f574d12de535f", false);
            Test("   032fa944-399a-4c04-9090-7ce1fd722a0d  ", "   d6d64cae9b40702f574d12de535f   ", false);
            Test("/032fa944-399a-4c04-9090-7ce1fd722a0d/capture-schedules/d6d64cae9b40702f574d12de535f", "", false);
            Test("   /032fa944-399a-4c04-9090-7ce1fd722a0d/capture-schedules/d6d64cae9b40702f574d12de535f/    ", "", false);
            Test("032fa944-399a-4c04-9090-7ce1fd722a0d,d6d64cae9b40702f574d12de535f", "", false);
            Test("   032fa944-399a-4c04-9090-7ce1fd722a0d  ,   d6d64cae9b40702f574d12de535f   ", "", false);
            Test("032fa944-399a-4c04-9090-7ce1fd722a0d	d6d64cae9b40702f574d12de535f", "", false);
            Test("   032fa944-399a-4c04-9090-7ce1fd722a0d  	   d6d64cae9b40702f574d12de535f   ", "", false);
            Test("032fa944-399a-4c04-9090-7ce1fd722a0d/d6d64cae9b40702f574d12de535f", "", false);
            Test("   032fa944-399a-4c04-9090-7ce1fd722a0d  /   d6d64cae9b40702f574d12de535f   ", "", false);

            Test("/032fa944-399a-4c04-9090-7ce1fd722a0d/capture-schedules/d6d64cae9b4074b5c02f574d12de535f", "d6d64cae9b40702f574d12de535f", false);
            Test("   /032fa944-399a-4c04-9090-7ce1fd722a0d/capture-schedules/d6d64cae9b4074b5c02f574d12de535f/    ", "d6d64cae9b40702f574d12de535f", false);
            Test("032fa944-399a-4c04-9090-7ce1fd722a0d,d6d64cae9b4074b5c02f574d12de535f", "d6d64cae9b40702f574d12de535f", false);
            Test("   032fa944-399a-4c04-9090-7ce1fd722a0d  ,   d6d64cae9b4074b5c02f574d12de535f   ", "d6d64cae9b40702f574d12de535f", false);
            Test("032fa944-399a-4c04-9090-7ce1fd722a0d	d6d64cae9b4074b5c02f574d12de535f", "d6d64cae9b40702f574d12de535f", false);
            Test("   032fa944-399a-4c04-9090-7ce1fd722a0d  	   d6d64cae9b4074b5c02f574d12de535f   ", "d6d64cae9b40702f574d12de535f", false);
            Test("032fa944-399a-4c04-9090-7ce1fd722a0d/d6d64cae9b4074b5c02f574d12de535f", "d6d64cae9b40702f574d12de535f", false);
            Test("   032fa944-399a-4c04-9090-7ce1fd722a0d  /   d6d64cae9b4074b5c02f574d12de535f   ", "d6d64cae9b40702f574d12de535f", false);

            Test("   032fa944-399a-4c04-9090-7ce1fd722a0d     d6d64cae9b4074b5c02f574d12de535f   ", "", true);

            return 0;

            Dictionary<string, List<Counts>> dict = new Dictionary<string, List<Counts>>();
            ProcessHelper proc = new ProcessHelper("git.exe", "log -n 1200 --pretty=\"%H %ae %ad\"");
            int maxAuthorLength = 0;
            foreach (string line in proc.Go())
            {
                string[] splits = line.Split(new char[] { ' ' });

                string commitId = splits[0];
                string author = StringHelper.TrimEnd(splits[1], "@microsoft.com");
                maxAuthorLength = Math.Max(maxAuthorLength, author.Length);

                GitOperations.GetCommitStats(commitId, out int fileCount, out int insertCount, out int deleteCount);
                if (!dict.ContainsKey(author))
                {
                    dict[author] = new List<Counts>();
                }
                dict[author].Add(new Counts(commitId, fileCount, insertCount, deleteCount));
                OldLogger.Log(".");
            }
            OldLogger.LogLine(" done");

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
                OldLogger.LogLine(("Author: " + author).PadRight(maxAuthorLength + 10) + fileCountString.PadRight(20) + insertCountString.PadRight(20) + deleteCountString.PadRight(20) + netCountString);
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

                OldLogger.LogLine(author.PadRight(maxAuthorLength + 10) + totalFileCount.ToString().PadRight(20) + totalInsertCount.ToString().PadRight(20) + totalDeleteCount.ToString().PadRight(20));
            }

            foreach (string author in dict.Keys)
            {
                OldLogger.LogLine("------------------------------------");
                OldLogger.LogLine("Author: " + author);
                List<Counts> list = dict[author];
                foreach(Counts count in list)
                {
                    OldLogger.LogLine(count.commit.PadRight(45) + count.files.ToString().PadRight(20) + count.inserts.ToString().PadRight(20) + count.deletes.ToString().PadRight(20));
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
                OldLogger.LogLine("Unable to revert " + filePath, OldLogger.LevelValue.Warning);
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
