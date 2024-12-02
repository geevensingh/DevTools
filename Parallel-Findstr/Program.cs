using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Parallel_Findstr
{
    internal class Program
    {
        public static bool CaseInsensitive { get; private set; } = false;
        public static bool SearchSubfolders { get; private set; } = false;
        public static List<string> FileSearchPatterns { get; } = new List<string>();
        public static List<string> TextSearchPatterns { get; private set; } = new List<string>();
        public static List<string> DirectoryExclusionPatterns { get; private set; } = new List<string>();

        static async Task Main(string[] args)
        {
            if (!ParseArgs(args))
            {
                PrintUsage();
                return;
            }

            if (CaseInsensitive)
            {
                TextSearchPatterns = TextSearchPatterns.Select(x => x.ToLower()).ToList();
            }

            SearchOption searchOption = SearchSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string currentDirectory = Directory.GetCurrentDirectory();

            var fileList = FileSearchPatterns.SelectMany(x => Directory.EnumerateFiles(currentDirectory, x, searchOption));

            foreach (var excludedDirectoryPattern in DirectoryExclusionPatterns)
            {
                fileList = fileList.Where(x => !x.ToLower().Contains("\\" + excludedDirectoryPattern + "\\"));
            }

            Console.WriteLine($"Files to search : {fileList.Count()}");
            foreach (var filePath in fileList)
            {
                string[] lines = null;
                try
                {
                    lines = File.ReadAllLines(filePath);
                }
                catch (Exception ex)
                {
                    string message = ex.Message;
                    if (!message.Contains(filePath))
                    {
                        message = $"Error reading file {filePath}: {message}";
                    }

                    Console.WriteLine(message);
                    continue;
                }
                for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++)
                {
                    string line = lines[lineNumber];
                    line = CaseInsensitive ? line.ToLower() : line;
                    if (TextSearchPatterns.Any(x => line.Contains(x)))
                    {
                        Console.WriteLine($"{filePath}({lineNumber + 1}): {line}");
                    }
                }
            }
        }

        private static bool ParseArgs(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("No arguments provided");
                return false;
            }

            for (int ii = 0; ii < args.Length; ii++)
            {
                string arg = args[ii];
                var temp = arg.Trim().ToLower();
                switch (temp)
                {
                    case "/i":
                        CaseInsensitive = true;
                        break;
                    case "/s":
                        SearchSubfolders = true;
                        break;
                    case "/c":
                        Debug.Assert(ii + 1 < args.Length, "No search string provided");
                        TextSearchPatterns.Add(args[++ii]);
                        break;
                    case "/f":
                        Debug.Assert(ii + 1 < args.Length, "No file string provided");
                        FileSearchPatterns.Add(args[++ii]);
                        break;
                    case "/xd":
                        Debug.Assert(ii + 1 < args.Length, "No directory exclusion provided");
                        DirectoryExclusionPatterns.Add(args[++ii]);
                        break;
                    case "/xbuild":
                        DirectoryExclusionPatterns.Add("bin");
                        DirectoryExclusionPatterns.Add("obj");
                        DirectoryExclusionPatterns.Add("objd");
                        DirectoryExclusionPatterns.Add("debug");
                        DirectoryExclusionPatterns.Add("retail");
                        DirectoryExclusionPatterns.Add(".vs");
                        break;
                    default:
                        Console.WriteLine("Unknown argument: " + arg);
                        return false;
                }
            }

            if (TextSearchPatterns.Count == 0)
            {
                Console.WriteLine("No search string provided");
                return false;
            }

            if (FileSearchPatterns.Count == 0)
            {
                Console.WriteLine("No file search pattern provided");
                return false;
            }

            return true;
        }

        private static bool IsValidTextSearchPattern(string arg)
        {
            return arg.All(c => !char.IsControl(c) && !char.IsSurrogate(c));
        }

        private static bool IsValidFileSearchPattern(string searchPattern)
        {
            var invalidChars = Path.GetInvalidPathChars().Union(Path.GetInvalidFileNameChars()).Except(new char[] { '*' });
            return !searchPattern.Any(c => invalidChars.Contains(c));
        }

        private static void PrintUsage()
        {
            throw new NotImplementedException();
        }
    }
}
