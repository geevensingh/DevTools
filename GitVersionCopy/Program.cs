namespace GitVersionCopy
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Utilities;

    class Program
    {
        static void Main(string[] args)
        {
            OldLogger.AnnounceStartStopActions = true;
            string masterBranch = "master";
            string fromVersion = null;
            string toVersion = null;

#if DEBUG
            fromVersion = "v8";
            toVersion = "v7";
            masterBranch = "0a4ad9aeb55b0397009558f772697f12ac668a18";
#endif

            if (args.Length == 2)
            {
                fromVersion = args[0];
                toVersion = args[1];
            }
            else if (args.Length == 3)
            {
                fromVersion = args[0];
                toVersion = args[1];
                masterBranch = args[2];
            }
            else
            {
                for (int ii = 0; ii < args.Length; ii++)
                {
                    string arg = args[ii].ToLower();
                    switch (arg)
                    {
                        case "/f":
                        case "/from":
                            fromVersion = args[++ii];
                            break;
                        case "/t":
                        case "/to":
                            toVersion = args[++ii];
                            break;
                        case "/m":
                        case "/master":
                        case "/base":
                            masterBranch = args[++ii];
                            break;
                        default:
                            Console.WriteLine("Unknown argument: " + arg);
                            return;
                    }
                }
            }

            if (string.IsNullOrEmpty(fromVersion))
            {
                Console.WriteLine("from what version?");
                return;
            }

            if (string.IsNullOrEmpty(toVersion))
            {
                Console.WriteLine("to what version?");
                return;
            }

            fromVersion = fromVersion.ToLower();
            toVersion = toVersion.ToLower();
            ApplyChanges(masterBranch, fromVersion, toVersion);
        }

        private static void ApplyChanges(string masterBranch, string fromVersion, string toVersion)
        {
            string gitRootDir = GitOperations.GetRoot();
            Debug.Assert(Directory.Exists(gitRootDir));

            string currentDir = Directory.GetCurrentDirectory();
            Debug.Assert(IOHelper.IsSubdirectory(gitRootDir, currentDir));

            string[] directoriesWithDifferences = GitOperations.GetDifferentFiles(masterBranch)
                .Select(path => path.ToLower())
                .Where(path => path.Contains("/" + fromVersion + "/"))
                .Select(path => path.Substring(0, path.IndexOf(fromVersion)))
                .Distinct()
                .ToArray();
            Dictionary<string, string> patchPaths = new Dictionary<string, string>();
            OldLogger.Start("Generating Patches", OldLogger.LevelValue.Normal);
            foreach (string directoryWithDifference in directoriesWithDifferences)
            {
                string patchPath = Path.Combine(gitRootDir, fromVersion + "." + directoryWithDifference.Replace('\\', '-').Replace('/', '-').Trim(new char[] { '-' }) + ".patch");

                // git format-patch --relative origin/master . --stdout
                ProcessHelper proc = new ProcessHelper(@"C:\Program Files\Git\cmd\git.exe", $"format-patch --relative {masterBranch} . --stdout");
                proc.WorkingDirectory = Path.Combine(gitRootDir, directoryWithDifference, fromVersion);
                proc.Go();
                File.WriteAllText(patchPath, string.Join("\n", proc.RawOutput));
                patchPaths.Add(directoryWithDifference, patchPath);
            }
            OldLogger.Stop("Generating Patches", OldLogger.LevelValue.Normal);


            for (int ii = 0; ii < directoriesWithDifferences.Length; ii++)
            {
                string directoryWithDifference = directoriesWithDifferences[ii];
                string destinationDirectory = Path.Combine(gitRootDir, directoryWithDifference, toVersion).Replace('\\', '/');
                string patchPath = patchPaths[directoryWithDifference];

                OldLogger.Start("Applying changes to " + destinationDirectory, OldLogger.LevelValue.Normal);
                // git apply --reject --directory=<destination-dir> --unsafe-paths <patch-path>
                ProcessHelper proc = new ProcessHelper("git.exe", $"apply --reject --directory={destinationDirectory} --unsafe-paths {patchPath}");
                proc.WorkingDirectory = gitRootDir;
                proc.Go();
                foreach (string line in proc.RawOutput)
                {
                    Console.WriteLine(line);
                }
                OldLogger.Stop("Applying changes to " + destinationDirectory, OldLogger.LevelValue.Normal);

                if (proc.RawOutput.Length > 0 && ii < directoriesWithDifferences.Length - 1)
                {
                    Console.WriteLine("Press any key to continue");
                    Console.ReadKey();
                }
            }
        }
    }
}
