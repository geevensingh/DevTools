using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Logging;

namespace Playlist_Generator
{
    class Program
    {
        static async Task Main(string[] argsArray)
        {
            ConsoleLogger.Instance.MinLevel = LogLevel.Normal;
#if DEBUG
            ConsoleLogger.Instance.MinLevel = LogLevel.Verbose;
#endif
            string zipFilePath = null;
            string playlistFilePath = null;
            List<string> argsList = argsArray.ToList();
            for (int ii = 0; ii < argsList.Count; ii++)
            {
                string arg = argsList[ii].ToLower();
                switch (arg)
                {
                    case "/v":
                    case "/verbose":
                        ConsoleLogger.Instance.MinLevel = LogLevel.Verbose;
                        argsList.RemoveAt(ii--);
                        break;
                    default:
                        if (arg.EndsWith(".playlist"))
                        {
                            playlistFilePath = arg;
                            argsList.RemoveAt(ii--);
                        }
                        else if (arg.EndsWith(".zip"))
                        {
                            zipFilePath = arg;
                            argsList.RemoveAt(ii--);
                        }
                        break;
                }
            }

            if (string.IsNullOrEmpty(zipFilePath))
            {
                zipFilePath = @"C:\Users\geevens\Downloads\logs_15461055.zip";
            }

            if (string.IsNullOrEmpty(playlistFilePath))
            {
                playlistFilePath = @"S:\Repos\SC.CST.Dunning\sln\generated.playlist";
            }

            await GeneratePlaylist(zipFilePath, playlistFilePath);
        }

        private static async Task GeneratePlaylist(string zipFilePath, string filePath)
        {
            Dictionary<ZipArchiveEntry, List<string>> failedTests = await GetFailedTestsPerArchiveEntry(zipFilePath);

            List<string> fullTestNames = await GetFullyQualifiedTestNames(failedTests);

            foreach (string testName in failedTests.Values.SelectMany(x => x))
            {
                int count = fullTestNames.Count(x => x.Split(new char[] { '.' }).Last() == testName);
                if (count == 0)
                {
                    Logger.Instance.LogLine($"Unable to find the namespace for {testName}", LogLevel.Warning);
                }
            }

            SavePlaylist(fullTestNames, filePath);
        }

        private static async Task<List<string>> GetFullyQualifiedTestNames(Dictionary<ZipArchiveEntry, List<string>> failedTests)
        {
            List<string> fullTestNames = new List<string>();
            Logger.Instance.LogLine("Looking for fully qualified failed test names...");
            foreach (ZipArchiveEntry entry in failedTests.Keys)
            {
                using (StreamReader reader = new StreamReader(entry.Open()))
                {
                    string line = await reader.ReadLineAsync();
                    while (line != null)
                    {
                        fullTestNames.AddRange(ProcessLineForTests(line, failedTests[entry]).Where(x => !fullTestNames.Contains(x)));
                        line = await reader.ReadLineAsync();
                    }
                }
            }

            fullTestNames.Sort();

            Logger.Instance.LogLine($"Found {fullTestNames.Count()} fully qualified test names:");
            foreach (string testName in fullTestNames)
            {
                Logger.Instance.LogLine(testName);
            }

            return fullTestNames;
        }

        private static void SavePlaylist(List<string> fullTestNames, string filePath)
        {
            StringBuilder playlistFile = new StringBuilder();
            playlistFile.AppendLine(@"<Playlist Version=""1.0"">");
            foreach (string fullTestName in fullTestNames)
            {
                string playlistLine = $"<Add Test=\"{fullTestName}\" />";
                playlistFile.AppendLine(playlistLine);
            }
            playlistFile.AppendLine(@"</Playlist>");
            File.WriteAllText(filePath, playlistFile.ToString(), Encoding.UTF8);
        }

        private static List<string> ProcessLineForTests(string line, List<string> failedTestNames)
        {
            List<string> fullTestNames = new List<string>();
            string[] splits = line.Split(new char[] { ' ', '.', '/', '\\', '<', '>' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string testName in failedTestNames)
            {
                if (splits.Contains(testName))
                {
                    if (GetFullyQualifiedTestName(line, testName, out string fullTestName)
                        && !fullTestNames.Contains(fullTestName))
                    {
                        fullTestNames.Add(fullTestName);
                    }
                }
            }

            return fullTestNames;
        }

        private static bool GetFullyQualifiedTestName(string line, string testName, out string fullTestName)
        {
            fullTestName = null;
            string[] spaceSplits = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Debug.Assert(DateTime.TryParse(spaceSplits[0], out DateTime lineDateTime));
            if (spaceSplits[1] == "Failed" || spaceSplits[1] == "Passed")
            {
                // Examples:
                // 2019-04-16T21:57:52.4159231Z Failed   Dunning_CaptureSchedule_CaptureStates_Upgrade_CaptureSucceedsOrAbandons
                Debug.Assert(spaceSplits[2] == testName);
                return false;
            }

            if (spaceSplits[1] == "##[error]" && spaceSplits[2] == "Test" && spaceSplits[3] == "method")
            {
                // Examples:
                // 2019-04-16T21:57:52.4159231Z ##[error] Test method CIT.Dunning.CaptureSchedule.Processor.VNext.CaptureScheduleTests.Dunning_Processor_LISMessage_Overrides_CaptureResult threw exception:
                line = string.Join(" ", spaceSplits, 1, spaceSplits.Length - 1).Trim();
                line = RemoveBefore(line, "Test method ");
                line = RemoveAfter(line, " threw exception");
                Debug.Assert(line.Contains(testName));
                fullTestName = line;
                return true;
            }

            // Examples:
            // 2019-04-17T16:48:32.8633499Z ##[error]   at CIT.Dunning.Service.V9.VersionConversionTests.<>c__DisplayClass1c2.<<Dunning_Convert_V3_V9_CaptureContext>b__1c1>d__1cb.MoveNext() in e:\agent_work\1\s\Product\Billing\test\Dunning.CIT\Service\V9\VersionConversionTests.cs:line 533
            line = string.Join(" ", spaceSplits, 1, spaceSplits.Length - 1).Trim();
            line = RemoveBetween(line, '<', '>');
            Regex regex = new Regex(@"\.([a-f]|[0-9])__([a-f]|[0-9])*\.MoveNext\(\)");
            line = regex.Replace(line, "");
            regex = new Regex(@"\.([a-f]|[0-9])__DisplayClass([a-f]|[0-9])*");
            line = regex.Replace(line, "");
            line = RemoveBefore(line, "at ");
            line = RemoveAfter(line, " in ");
            if (!line.Contains("__"))
            {
                fullTestName = $"{line}.{testName}";
                return true;
            }

            return false;
        }

        private static async Task<Dictionary<ZipArchiveEntry, List<string>>> GetFailedTestsPerArchiveEntry(string zipFilePath)
        {
            Logger.Instance.LogLine("Looking for failed tests...");

            Dictionary<ZipArchiveEntry, List<string>> failedTests = new Dictionary<ZipArchiveEntry, List<string>>();
            foreach (ZipArchiveEntry entry in ZipFile.Open(zipFilePath, ZipArchiveMode.Read)
                .Entries
                .Where(x => x.FullName.EndsWith("testAssemblies.txt")))
            {
                using (StreamReader reader = new StreamReader(entry.Open()))
                {
                    //// line = @"2019-04-16T20:49:32.8565518Z Failed   Dunning_CaptureSchedule_CaptureStates_Upgrade_CaptureSucceedsOrAbandons";
                    string line = await reader.ReadLineAsync();
                    while (line != null)
                    {
                        string[] spaceSplits = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (spaceSplits.Length == 3
                            && DateTime.TryParse(spaceSplits[0], out DateTime lineDateTime)
                            && spaceSplits[1] == "Failed")
                        {
                            string testName = spaceSplits[2];
                            if (!failedTests.ContainsKey(entry))
                            {
                                failedTests.Add(entry, new List<string>());
                            }

                            failedTests[entry].Add(testName);
                        }

                        line = await reader.ReadLineAsync();
                    }
                }
            }

            IEnumerable<string> allFailedTests = failedTests.Values.SelectMany(x => x);
            Logger.Instance.LogLine($"Found {allFailedTests.Count()} failed tests:");
            foreach (string testName in allFailedTests)
            {
                Logger.Instance.LogLine(testName);
            }

            return failedTests;
        }

        private static string RemoveAfter(string str, string subStr)
        {
            int index = str.IndexOf(subStr);
            if (index == -1)
            {
                return str;
            }

            return str.Substring(0, index);
        }

        private static string RemoveBefore(string str, string subStr)
        {
            int index = str.IndexOf(subStr);
            if (index == -1)
            {
                return str;
            }

            return str.Substring(index + subStr.Length);
        }

        private static string RemoveBetween(string str, char startChar, char endChar)
        {
            int depth = 1;
            int startIndex = str.IndexOf(startChar);
            if (startIndex == -1)
            {
                return str;
            }
            int endIndex = str.LastIndexOf(endChar);
            if (endIndex == -1)
            {
                return str;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(str.Substring(0, startIndex));
            int nextStartIndex = startIndex;
            for (int ii = startIndex; ii <= endIndex; ii++)
            {
                if (str[ii] == startChar)
                {
                    if (depth == 1)
                    {
                        sb.Append(str.Substring(nextStartIndex, ii - nextStartIndex));
                    }
                    depth++;
                }
                else if (str[ii] == endChar)
                {
                    depth--;
                    nextStartIndex = ii + 1;
                }
            }
            sb.Append(str.Substring(nextStartIndex));
            return sb.ToString();
        }
    }
}
