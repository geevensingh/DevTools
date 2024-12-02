using System;
using System.Collections;
using System.Collections.Concurrent;
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
            var output = new ConcurrentDictionary<string, IEnumerable<string>>();
            var tasks = new List<Task>();
            var lookup = new Dictionary<int, string>();
            foreach (var filePath in fileList)
            {
                tasks.Add(Task.Run(() =>
                {
                    var fileOutput = SearchFile(filePath);
                    if (fileOutput.Count() > 0)
                    {
                        Debug.Assert(output.TryAdd(filePath, fileOutput));
                    }
                }));
                lookup.Add(tasks.Last().Id, filePath);
            }

            //int ii = 0;
            while (!Task.WhenAll(tasks).IsCompleted)
            {
                Console.WriteLine($"Not done yet : {output.Count} / {tasks.Count(x => x.IsCompleted)}");
                //ii++;
                //if (ii > 5)
                //{
                //    foreach (var task in tasks.Where(x => !x.IsCompleted))
                //    {
                //        Console.WriteLine($"Task {task.Id} is not done");
                //    }
                //}
                await Task.Delay(1000);
            }

            await Task.WhenAll(tasks);
            foreach (var kvp in output)
            {
                if (kvp.Value.Count() == 0)
                {
                    continue;
                }

                Console.WriteLine(kvp.Key);
                //Console.WriteLine($"\r\r{kvp.Value.Count()}");
                foreach (var line in kvp.Value)
                {
                    Console.WriteLine(line);
                }
            }
        }

        private static IEnumerable<string> SearchFile(string filePath)
        {
            var output = new List<string>();
            try
            {
                using (TextReader reader = new StreamReader(filePath))
                {
                    string line;
                    int lineNumber = 0;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lineNumber++;
                        if (line.ToCharArray().Any(x => (char.IsControl(x) && x != '\t') || x == '\0'))
                        {
                            Debug.Assert(output.Count == 0);
                            return output;
                        }
                        line = CaseInsensitive ? line.ToLower() : line;
                        if (TextSearchPatterns.Any(x => line.Contains(x)))
                        {
                            output.Add($"{lineNumber}\t: {line}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Assert(output.Count == 0);
                string message = ex.Message;
                if (!message.Contains(filePath))
                {
                    message = $"Error reading file {filePath}: {message}";
                }

                output.Add(message);
                return output;
            }

            return output;
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
