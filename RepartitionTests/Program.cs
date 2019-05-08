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
        static void Main(string[] args)
        {
            ////RemoveTrailingWhitespace(@"S:\Repos\SC.CST.Dunning", "cs");

            const char initialPartition = 'A';
            const char maxPartition = 'H';
            char currentPartition = initialPartition;
            Dictionary<char, List<string>> testsByPartition = new Dictionary<char, List<string>>();
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
                    if (lines[ii].Trim().StartsWith("[TestCategory(\"CaptureTestPartition"))
                    {
                        lines.RemoveAt(ii--);
                    }

                    if (lines[ii].Trim() == "[TestMethod]")
                    {
                        lines[ii] = lines[ii].TrimEnd();

                        if (!testsByPartition.ContainsKey(currentPartition))
                        {
                            testsByPartition.Add(currentPartition, new List<string>());
                        }
                        testsByPartition[currentPartition].Add(lines[ii + 1]);

                        lines.Insert(ii, lines[ii++].Replace("TestMethod", $"TestCategory(\"CaptureTestPartition{currentPartition++}\")"));
                        if (currentPartition == maxPartition)
                        {
                            currentPartition = initialPartition;
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

            foreach (char partition in testsByPartition.Keys)
            {
                Console.WriteLine($"{partition}, {testsByPartition[partition].Count}, {testsByPartition[partition].Count(x => x.Contains(" Dunning_CaptureSchedule_"))}");
            }
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
