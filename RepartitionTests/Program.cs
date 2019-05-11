using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RepartitionTests
{
    class Program
    {
        static char[] partitions = new char[] { 'A', 'B', 'C', 'D', 'E', 'F', 'G' };

        static void Main(string[] args)
        {
            ////RemoveTrailingWhitespace(@"S:\Repos\SC.CST.Dunning", "cs");

            bool clean = false;

            foreach (string filePath in GetAllFiles(@"S:\Repos\SC.CST.Dunning\Product\Billing\test\Dunning.CIT", new string[] { "cs" }))
            {
                if (filePath.ToLower().EndsWith("orderedtests.cs"))
                {
                    continue;
                }

                string initialText = File.ReadAllText(filePath);
                List<string> lines = new List<string>(initialText.Split(new string[] { "\r\n" }, StringSplitOptions.None));
                for (int ii = 0; ii < lines.Count; ii++)
                {
                    if (clean && lines[ii].Trim().StartsWith("[TestCategory(\"CaptureTestPartition"))
                    {
                        lines.RemoveAt(ii--);
                    }

                    if (lines[ii].Trim() == "[TestMethod]")
                    {
                        lines[ii] = lines[ii].TrimEnd();

                        AddTest(out char partition, lines[ii + 1]);

                        string partitionLine = lines[ii].Replace("TestMethod", $"TestCategory(\"CaptureTestPartition{partition}\")");
                        if (lines[ii - 1].Trim().StartsWith("[TestCategory(\"CaptureTestPartition"))
                        {
                            lines[ii - 1] = partitionLine;
                        }
                        else
                        {
                            lines.Insert(ii++, partitionLine);
                        }
                    }
                }

                string finalText = string.Join("\r\n", lines);
                if (initialText != finalText)
                {
                    for (int ii = 0; ii < lines.Count; ii++)
                    {
                        lines[ii] = lines[ii].TrimEnd();
                    }
                    Utilities.IOHelper.WriteAllText(filePath, string.Join("\r\n", lines));
                }
            }

            foreach (char partition in lookupTestByPartition.Keys)
            {
                Console.WriteLine($"{partition}, {lookupTestByPartition[partition].Count}, {lookupTestByPartition[partition].Count(x => x.StartsWith("Dunning_CaptureSchedule_"))}");
            }
        }


        private static Dictionary<char, List<string>> lookupTestByPartition = null;
        private static Dictionary<string, char> lookupPartitionByTest = new Dictionary<string, char>();
        private static void AddTest(out char partition, string testLine)
        {
            Debug.Assert(testLine.Contains( "public "));
            string test = testLine;
            test = test.Trim();
            int indexOfOpenParen = test.IndexOf('(');
            if (indexOfOpenParen > 0)
            {
                test = test.Substring(0, indexOfOpenParen);
            }
            int indexOfLastSpace = test.LastIndexOf(' ');
            if (indexOfLastSpace > 0)
            {
                test = test.Substring(indexOfLastSpace + 1);
            }

            if (lookupTestByPartition == null)
            {
                lookupTestByPartition = new Dictionary<char, List<string>>();
                foreach (char temp in partitions)
                {
                    lookupTestByPartition.Add(temp, new List<string>());
                }
            }

            if (lookupPartitionByTest.TryGetValue(test, out char existingPartition))
            {
                partition = existingPartition;
            }
            else
            {
                partition = lookupTestByPartition.Keys.OrderBy(key => lookupTestByPartition[key].Count).First();
            }

            lookupTestByPartition[partition].Add(test);
            lookupPartitionByTest[test] = partition;
        }

        private static string GenerateParitionLine(string testMethodLine, char partition)
        {
            return testMethodLine.Replace("TestMethod", $"TestCategory(\"CaptureTestPartition{partition}\")");
        }


        private static void RemoveTrailingWhitespace(string root, string extension)
        {
            foreach (string file in GetAllFiles(root, new string[] { extension }))
            {
                ReplaceAll(file, " \r\n", "\r\n");
            }
        }

        private static void ReplaceAll(string filePath, string oldValue, string newValue)
        {
            Debug.Assert(oldValue.Length != newValue.Length);

            string text = File.ReadAllText(filePath);
            int oldTextLength = text.Length - 1;
            while (oldTextLength != text.Length)
            {
                oldTextLength = text.Length;
                text = text.Replace(oldValue, newValue);
            }

            File.WriteAllText(filePath, text, Utilities.IOHelper.GetEncoding(filePath));
        }

        static string[] GetAllFiles(string root, string[] extensions)
        {
            List<string> allFiles = new List<string>();
            foreach (string ext in extensions)
            {
                allFiles.AddRange(Directory.EnumerateFiles(root, @"*." + ext, SearchOption.AllDirectories));
            }
            return allFiles.ToArray();
        }
    }
}
