using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace InvariantStringCleanup
{
    class Program
    {
        static void Main(string[] args)
        {
            List<string> allFiles = new List<string>(GetAllFiles(new string[] { "cs" }));
            //allFiles = new List<string>()
            //{
            //    @"D:\Repos\SC.CST.Dunning\Product\Billing\test\Common.CIT\Services\Billing\20190228\BillingSchemaUnitTests.cs",
            //    @"D:\Repos\SC.CST.Dunning\Product\Billing\test\Common.CIT\Storage\PartitionedKeyValueStoreTests.cs"
            //    @"D:\Repos\SC.CST.Dunning\Product\Billing\Common\Services\Billing\20180630\FeeInstruction.cs",
            //    @"D:\Repos\SC.CST.Dunning\Product\Billing\shared\Common\Storage\SqlKeyValueStore.cs",
            //    @"D:\Repos\SC.CST.Dunning\Product\Billing\shared\Common\ValidationContext.cs",
            //    @"D:\Repos\SC.CST.Dunning\Product\Billing\Common\Services\Billing\20170330\BillingServiceClient.cs",
            //    @"D:\Repos\SC.CST.Dunning\Product\Billing\shared\Journal.Reader\V4\ConcurrentReaderService.cs",
            //    @"D:\Repos\SC.CST.Dunning\Product\Billing\test\Common.CIT\SqlTransientFaultTest.cs",
            //};

            allFiles.Clear();
            allFiles.Add(@"D:\Repos\SC.Commerce.Invoicing\Invoicing\Tests\Invoicing.Tests.Service\V8\PaynowWithPSD2Tests.cs");

            foreach (string file in allFiles)
            {
                string text = File.ReadAllText(file);
                const string startPattern = "InvariantString.Format(";
                int startIndex = text.IndexOf(startPattern);
                while (startIndex != -1)
                {
                    int endIndex = FindMatchingParen(text, startIndex);
                    string oldText = text.Substring(startIndex, endIndex - startIndex + 1);
                    string formatPattern = FindFirstQuote(oldText, startPattern.Length, out int formatPatternEndIndex);
                    if (formatPattern != null)
                    {
                        Debug.Assert(formatPattern.StartsWith("\""));
                        Debug.Assert(formatPattern.EndsWith("\""));

                        string formatArguments = oldText.Substring(formatPatternEndIndex + 1);
                        formatArguments = formatArguments.Substring(0, formatArguments.Length - 1);

                        string[] formatArgs = GetCommaSplits(formatArguments);
                        Debug.Assert(formatArgs.Length > 0);
                        Debug.Assert(formatArgs[0] == string.Empty);
                        if (formatArgs.Length > 1)
                        {
                            Debug.Assert(formatPattern.Contains("{" + (formatArgs.Length - 2) + "}"));
                            Debug.Assert(!formatPattern.Contains("{" + (formatArgs.Length - 1) + "}"));

                            for (int jj = 1; jj < formatArgs.Length; jj++)
                            {
                                formatArgs[jj] = formatArgs[jj].Trim();
                                const string trimmedSuffix = ".ToString()";
                                if (formatArgs[jj].EndsWith(trimmedSuffix))
                                {
                                    formatArgs[jj] = formatArgs[jj].Substring(0, formatArgs[jj].Length - trimmedSuffix.Length);
                                }
                                formatPattern = formatPattern.Replace("{" + (jj - 1) + "}", "{" + formatArgs[jj] + "}");
                            }
                            formatPattern = "$" + formatPattern;
                        }

                        text = text.Remove(startIndex, endIndex - startIndex + 1);
                        text = text.Insert(startIndex, formatPattern);
                    }

                    startIndex = text.IndexOf(startPattern, startIndex + 1);
                }

                File.WriteAllText(file, text, Utilities.IOHelper.GetEncoding(file));
            }
        }

        private static string FindFirstQuote(string text, int startIndex, out int endIndex)
        {
            endIndex = -1;

            int firstQuoteIndex = text.Replace("\\\"", "--").IndexOf("\"", startIndex);
            if (firstQuoteIndex == -1)
            {
                return null;
            }

            string prefix = text.Substring(startIndex, firstQuoteIndex - startIndex);
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                return null;
            }

            endIndex = text.Replace("\\\"", "--").IndexOf("\"", firstQuoteIndex + 1);
            return text.Substring(firstQuoteIndex, endIndex - firstQuoteIndex + 1);
        }

        private static int FindMatchingParen(string text, int startIndex)
        {
            startIndex = text.IndexOf("(", startIndex);
            int depth = 1;
            for (int ii = startIndex + 1; ii < text.Length; ii++)
            {
                switch (text[ii])
                {
                    case '(':
                        depth++;
                        break;
                    case ')':
                        depth--;
                        if (depth == 0)
                        {
                            return ii;
                        }
                        break;
                }
            }

            throw new Exception("Failed");
        }

        private static string[] GetCommaSplits(string text)
        {
            List<int> indexes = new List<int>();
            bool isInString = false;
            int parenDepth = 0;
            for (int ii = 0; ii < text.Length; ii++)
            {
                switch (text[ii])
                {
                    case '(':
                        if (!isInString)
                        {
                            parenDepth++;
                        }
                        break;
                    case ')':
                        if (!isInString)
                        {
                            parenDepth--;
                        }
                        break;
                    case '\"':
                        isInString = !isInString;
                        break;
                    case ',':
                        if (!isInString && parenDepth == 0)
                        {
                            indexes.Add(ii);
                        }
                        break;
                }
            }

            return GetSplits(text, indexes.ToArray());
        }

        private static string[] GetSplits(string text, int[] indexes)
        {
            List<string> results = new List<string>();
            int lastIndex = -1;
            for (int ii = 0; ii < indexes.Length; ii++)
            {
                int index = indexes[ii];
                results.Add(text.Substring(lastIndex + 1, index - lastIndex - 1));
                lastIndex = index;
            }
            results.Add(text.Substring(lastIndex + 1, text.Length - lastIndex - 1));
            return results.ToArray();
        }

        static string[] GetAllFiles(string[] extensions)
        {
            List<string> allFiles = new List<string>();
            foreach (string ext in extensions)
            {
                allFiles.AddRange(Directory.EnumerateFiles(@"D:\Repos\SC.CST.Dunning\Product\Billing\test", @"*." + ext, SearchOption.AllDirectories));
            }
            return allFiles.ToArray();
        }
    }

}
