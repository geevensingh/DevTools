using System.Collections.Generic;
using System.IO;
using System.Diagnostics;

namespace SearchAndReplace
{
    class Program
    {
        static void Main(string[] args)
        {
            List<string> allFiles = new List<string>(GetAllFiles(new string[] { "h", "cpp", "idl" }));

            foreach (string file in allFiles)
            {
                string text = File.ReadAllText(file);
                int oldTextLength = text.Length - 1;
                while (oldTextLength != text.Length)
                {
                    oldTextLength = text.Length;
                    text = text.Replace(" \r\n", "\r\n");
                }

                File.WriteAllText(file, text, Utilities.IOHelper.GetEncoding(file));
            }
        }

        static string[] GetAllFiles(string[] extensions)
        {
            List<string> allFiles = new List<string>();
            foreach (string ext in extensions)
            {
                allFiles.AddRange(Directory.EnumerateFiles(@"S:\Repos\media.app_2\src", @"*." + ext, SearchOption.AllDirectories));
            }
            return allFiles.ToArray();
        }

    }
}
