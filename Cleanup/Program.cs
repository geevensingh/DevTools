using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Utilities;

namespace Cleanup
{
    class Program
    {
        static void Main(string[] args)
        {
            //List<GitFileInfo> toBeRemoved = new List<GitFileInfo>();
            //GitFileInfo[] gitInfos = GitStatus.Get().GetFiles(false /*staged*/);
            //foreach (GitFileInfo gitInfo in gitInfos)
            //{
            //    if (gitInfo.FileState == FileState.Modified && File.Exists(gitInfo.FilePath))
            //    {
            //        FileInfo fileInfo = new FileInfo(gitInfo.FilePath);
            //        if (fileInfo.Length < 4)
            //        {
            //            Logger.LogLine("Deleted: " + gitInfo.FilePath);
            //            toBeRemoved.Add(gitInfo);
            //        }
            //    }
            //}

            List<string> toBeRemoved = new List<string>();
            List<string> possiblyRemovedFiles = new List<string>(GetAllFiles(new string[] { "h", "cpp", "xaml" }));
            foreach (string possiblyRemovedFilePath in possiblyRemovedFiles)
            {
                if (File.Exists(possiblyRemovedFilePath))
                {
                    FileInfo fileInfo = new FileInfo(possiblyRemovedFilePath);
                    if (fileInfo.Length < 4)
                    {
                        OldLogger.LogLine("Deleted: " + possiblyRemovedFilePath);
                        File.Delete(possiblyRemovedFilePath);
                        toBeRemoved.Add(Path.GetFileName(possiblyRemovedFilePath));
                    }
                }
            }


            //List<string> allFiles = new List<string>(GetAllFiles(new string[] { "h", "cpp", "vcxproj", "filters" }));
            List<string> allFiles = new List<string>(GetAllFiles(new string[] { "vcxproj", "vcxitems", "vcxproj.filters", "vcxitems.filters" }));
            foreach (string file in allFiles)
            {
                List<string> removedLines = new List<string>();
                List<string> lines = new List<string>(File.ReadAllLines(file));
                for (int ii = 0; ii < lines.Count; ii++)
                {
                    //foreach (GitFileInfo gitInfo in toBeRemoved)
                    foreach(string removedFilePath in toBeRemoved)
                    {
                        if (lines[ii].ToLower().Contains(removedFilePath.ToLower()))
                        {
                            removedLines.Add(lines[ii]);
                            lines.RemoveAt(ii);
                            ii--;
                            break;
                        }
                    }
                }
                if (removedLines.Count > 0)
                {
                    OldLogger.LogLine(@"Lines to be removed from " + file + " :");
                    foreach(string removedLine in removedLines)
                    {
                        OldLogger.LogLine("\t" + removedLine.Trim());
                    }
                    File.WriteAllLines(file, lines, Utilities.IOHelper.GetEncoding(file));
                }
            }
        }

        static string[] GetAllFiles(string[] extensions)
        {
            List<string> allFiles = new List<string>();
            foreach (string ext in extensions)
            {
                allFiles.AddRange(Directory.EnumerateFiles(Environment.CurrentDirectory, @"*." + ext, SearchOption.AllDirectories));
            }
            return allFiles.ToArray();
        }

    }
}
