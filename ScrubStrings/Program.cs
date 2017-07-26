using System;
using System.Collections.Generic;
using System.Xml;
using System.Diagnostics;
using System.IO;

namespace ScrubStrings
{
    class Program
    {
        static void Main(string[] args)
        {
            string rootDirectory = args.Length > 0 ? args[0] : "";
            if (!Directory.Exists(rootDirectory))
            {
                rootDirectory = Environment.GetEnvironmentVariable("REPO_ROOT");
            }
#if DEBUG
            if (!Directory.Exists(rootDirectory))
            {
                rootDirectory = @"S:\Repos\media.app\src";
            }
#endif
            if (!Directory.Exists(rootDirectory))
            {
                Console.WriteLine("Please specify a directory.");
            }
            List<string> stringIds = new List<string>();
            foreach (string filePath in Directory.GetFiles(rootDirectory, "*.resw", SearchOption.AllDirectories))
            {
                // Ignore the error strings.
                if (filePath.ToLower().Contains(@"\resw\errorstrings\resources.resw"))
                {
                    continue;
                }

                // Let's only search for strings in a single language - maybe English.
                if (!filePath.ToLower().Contains(@"\en-us\"))
                {
                    continue;
                }

                XmlDocument doc = new XmlDocument();
                doc.Load(filePath);

                Debug.Assert(doc.SelectSingleNode(@"root/resheader[@name='resmimetype']/value").InnerText == "text/microsoft-resx");
                Debug.Assert(doc.SelectSingleNode(@"root/resheader[@name='version']/value").InnerText == "2.0");
                Debug.Assert(doc.SelectSingleNode(@"root/resheader[@name='reader']/value").InnerText == "System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
                Debug.Assert(doc.SelectSingleNode(@"root/resheader[@name='writer']/value").InnerText == "System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");

                XmlNodeList dataNodes = doc.SelectNodes(@"root/data");
                foreach (XmlNode dataNode in dataNodes)
                {
                    stringIds.Add(dataNode.Attributes["name"].Value);
                }
            }

            FindString(rootDirectory, ref stringIds);
            foreach (string stringId in stringIds)
            {
                Console.WriteLine(stringId);
            }
        }

        private static int initialCursorLeft = -1;
        private static int lastPercent = 0;
        private static void StartProgress()
        {
            Debug.Assert(initialCursorLeft == -1);
            initialCursorLeft = Console.CursorLeft;
            Console.Write(lastPercent.ToString().PadLeft(3) + "%");
        }
        private static void UpdateProgress(int percent)
        {
            Debug.Assert(0 <= percent);
            Debug.Assert(percent <= 100);
            Debug.Assert(initialCursorLeft != -1);
            if (percent != lastPercent)
            {
                lastPercent = percent;
                Console.CursorLeft = initialCursorLeft;
                Console.Write(lastPercent.ToString().PadLeft(3) + "%");
                if (percent == 100)
                {
                    Console.WriteLine();
                    initialCursorLeft = -1;
                }
            }
        }

        private static void FindString(string directoryPath, ref List<string> stringIds)
        {
            List<string> files = new List<string>();
            foreach (string extension in new string[] { "cpp", "h", "xml" })
            {
                foreach (string filePath in Directory.GetFiles(directoryPath, "*." + extension, SearchOption.AllDirectories))
                {
                    files.Add(filePath);
                }
            }
            StartProgress();
            int mod = files.Count / 200;
            for (int ii = 0; ii < files.Count; ii++)
            {
                if (ii % mod == 0)
                {
                    UpdateProgress(ii * 100 / files.Count);
                }
                Console.Write(".");
                string filePath = files[ii];
                //Console.WriteLine("Searching " + filePath);
                foreach (string line in File.ReadAllLines(filePath))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        List<string> found = new List<string>();
                        foreach (string stringId in stringIds)
                        {
                            if (line.Contains(stringId))
                            {
                                found.Add(stringId);
                            }
                        }
                        foreach (string foundStringId in found)
                        {
                            stringIds.Remove(foundStringId);
                        }
                    }
                }
            }
        }
    }
}
