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
        static int Main(string[] args)
        {
#if false
            string originalFilePath = @"S:\Repos\Sumner\zune\client\xaml\temp.txt";
            List<string> lines = new List<string>(File.ReadAllLines(originalFilePath));
            for (int ii = 0; ii < lines.Count; ii++)
            {
                string line = lines[ii];
                if (line == "\"")
                {
                    lines.RemoveAt(ii--);
                    continue;
                }
                line = line.TrimStart(new char[] { '\"' });
                line = line.Replace("\"\"", "\"");
                lines[ii] = line;
            }
            lines.InsertRange(0, File.ReadAllLines(@"s:\prefix.txt"));
            lines.AddRange(File.ReadAllLines(@"s:\suffix.txt"));
            string newFile = originalFilePath.Replace(@".txt", @".xml");
            File.WriteAllLines(newFile, lines.ToArray(), Encoding.UTF8);
            File.Copy(newFile, originalFilePath.Replace(@"temp.txt", @"strings\en-US\resw\migrationStrings\resources.resw"), true /*overwrite*/);
#endif
            foreach (string filePath in GetAllFiles(@"S:\Repos\media.app\src\Generated\Idl\EntPlat.Idl", new string[] { "h"}))
            {
                ProcessHelper proc = new ProcessHelper("git.exe", @"diff --unified=0 " + filePath);
                string[] lines = proc.Go();
                if (proc.ExitCode != 0 || proc.StandardError.Length != 0)
                {
                    Logger.LogLine("Unable to diff " + filePath);
                    return -1;
                }

                bool foundChange = false;
                for (int ii = 0; ii < lines.Length; ii++)
                {
                    if (lines[ii].StartsWith("@@") && ii + 2 < lines.Length)
                    {
                        if (lines[ii+1].StartsWith(@"-/* Compiler settings for") && lines[ii + 2].StartsWith(@"+/* Compiler settings for"))
                        {
                            continue;
                        }
                        foundChange = true;
                    }
                }
                if (!foundChange)
                {
                    RevertFile(filePath);
                    RevertFile(StringHelper.TrimEnd(filePath, @".h") + @".winmd");
                }
            }
            return 0;
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
