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
    // Commands to try:
    // /i /s /c aircapi /f * /xbuild
    // /xbuild /i /c "Not able to find previous unbilled lineItem" /s /f *
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
            var output = new ConcurrentDictionary<string, IEnumerable<Match>>();
            var tasks = new List<Task>();
            var lookup = new Dictionary<int, string>();
            foreach (var filePath in fileList)
            {
                tasks.Add(Task.Run(() =>
                {
                    var fileOutput = SearchFile(filePath);
                    if (fileOutput.Count() > 0)
                    {
                        while (!output.TryAdd(filePath, fileOutput))
                        {
                            Debug.Fail("Why did TryAdd fail?");
                        }
                    }
                }));
                lookup.Add(tasks.Last().Id, filePath);
            }

            await ReportTaskProgress(tasks);

            await Task.WhenAll(tasks);
            foreach (var kvp in output)
            {
                if (kvp.Value.Count() == 0)
                {
                    continue;
                }

                Console.WriteLine(kvp.Key);
                //Console.WriteLine($"\r\r{kvp.Value.Count()}");
                foreach (var match in kvp.Value)
                {
                    match.ToConsole();
                }
            }
        }

        private static async Task ReportTaskProgress(List<Task> tasks)
        {
            int taskCount = tasks.Count;
            int tasksCompleted = tasks.Count(x => x.IsCompleted);
            while (tasksCompleted < taskCount)
            {
                string message = $"Tasks completed : {tasksCompleted} / {taskCount}";
                Console.Write(message);
                await Task.Delay(1000);
                string antiMessage = new string('\b', message.Length);
                Console.Write(antiMessage);

                tasksCompleted = tasks.Count(x => x.IsCompleted);
            }
            Console.WriteLine("Tasks complete");
        }

        private static IEnumerable<Match> SearchFile(string filePath)
        {
            var output = new List<Match>();
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
                        string lineToCompare = CaseInsensitive ? line.ToLower() : line;
                        foreach (string pattern in TextSearchPatterns)
                        {
                            int index;
                            int startIndex = 0;
                            while ((index = lineToCompare.IndexOf(pattern, startIndex)) != -1)
                            {
                                output.Add(new Match
                                {
                                    FilePath = filePath,
                                    LineNumber = lineNumber,
                                    Line = line,
                                    StartIndex = index,
                                    EndIndex = index + pattern.Length
                                });
                                startIndex = index + pattern.Length;
                            }
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

                output.Add(new Match
                {
                    FilePath = filePath,
                    LineNumber = -1,
                    Line = message,
                    StartIndex = 0,
                    EndIndex = message.Length - 1
                });
                return output;
            }

            return output;
        }

        private class Match
        {
            public string FilePath { get; set; }
            public int LineNumber { get; set; }
            public string Line { get; set; }
            public int StartIndex { get; set; }
            public int EndIndex { get; set; }

            public void ToConsole()
            {
                var originalConsoleForegroundColor = Console.ForegroundColor;
                string preHighlight = $"{this.LineNumber} \t: {Line.Substring(0, this.StartIndex)}";
                string highlight = this.Line.Substring(this.StartIndex, this.EndIndex - this.StartIndex);
                string postHighlight = this.Line.Substring(this.EndIndex);
                Console.Write(preHighlight);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write(highlight);
                Console.ForegroundColor = originalConsoleForegroundColor;
                Console.WriteLine(postHighlight);
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

        private static void PrintUsage()
        {
            Console.WriteLine(@"
/i                        : Case insensitive search
/s                        : Search subfolders
/c <search string>        : Text to search for
/f <file search pattern>  : File search pattern
/xd <directory exclusion> : Exclude directories
/xbuild                   : Exclude build directories
");
        }
    }
}
