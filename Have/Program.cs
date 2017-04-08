using System;
using System.Diagnostics;
using Utilities;

namespace Have
{
    class Program
    {
        static void Main(string[] args)
        {
            string path = @"...";
            bool ignoreCase = true;
            string search = string.Empty;
            bool relativePath = false;

            for (int ii = 0; ii < args.Length; ii++)
            {
                // handle other command line args
                string arg = args[ii];
                if (arg.StartsWith("/") || arg.StartsWith("-"))
                {
                    switch (arg.Substring(1).ToLower())
                    {
                        case "path":
                            path = args[++ii];
                            break;
                        case "ic":
                        case "ignorecase":
                            ignoreCase = true;
                            break;
                        case "c":
                        case "case":
                        case "casesensitive":
                            ignoreCase = false;
                            break;
                        case "rel":
                        case "relative":
                        case "relativepath":
                            relativePath = true;
                            break;
                        default:
                            Console.WriteLine("Unknown argument: " + arg);
                            PrintUsage();
                            return;
                    }
                }
                else
                {
                    search = arg;
                }
            }

            string currentDir = System.IO.Directory.GetCurrentDirectory() + @"\";

            Console.WriteLine("path: " + path);
            Console.WriteLine("ignoreCase: " + ignoreCase);
            Console.WriteLine("search: " + search);
            Console.WriteLine("relativePath: " + relativePath);
            Console.WriteLine("currentDir: " + currentDir);

            StringComparison comparison = ignoreCase ? StringComparison.InvariantCultureIgnoreCase : StringComparison.InvariantCulture;

            SDOperations sd = new SDOperations(@"redmond\geevens");
            Console.WriteLine("SDRoot: " + sd.SDRoot);

            Console.WriteLine();
            Logger.AnnounceStartStopActions = false;

            SDOperations.File[] files = sd.GetFiles(currentDir, path);
            foreach (SDOperations.File file in files)
            {
                var localPath = file.LocalPath;
                Debug.Assert(localPath.ToLower().StartsWith(currentDir.ToLower()));
                if (relativePath)
                {
                    localPath = localPath.Substring(currentDir.Length);
                }
                if (localPath.IndexOf(search, comparison) != -1)
                {
                    Console.WriteLine(localPath);
                }
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("Print something about usage");
        }
    }
}
