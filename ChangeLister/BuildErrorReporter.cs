using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using Utilities;

namespace ChangeLister
{
    class BuildErrorReporter
    {
        static Dictionary<string, string[]> cache = new Dictionary<string, string[]>();
        public static string[] generateErrorReport(Client client)
        {
            if (!client.IsLocal)
            {
                return new string[0];
            }

            if (!cache.ContainsKey(client.Name))
            {
                OldLogger.Start("generateErrorReport - " + client.Path);
                List<string> result = new List<string>();
#if false
                SearchOption searchOption = SearchOption.AllDirectories;
#if DEBUG
                searchOption = SearchOption.TopDirectoryOnly;
#endif
                List<string> errorFiles = new List<string>(Directory.EnumerateFiles(client.Path, "build*.err", searchOption));
                for (int ii = 0; ii < errorFiles.Count; ii++)
                {
                    Debug.Assert(File.Exists(errorFiles[ii]));
                    result.Add("Build error file found at:" + errorFiles[ii]);
                }
                errorFiles = new List<string>(Directory.EnumerateFiles(client.Path, "build*.wrn", searchOption));
                for (int ii = 0; ii < errorFiles.Count; ii++)
                {
                    Debug.Assert(File.Exists(errorFiles[ii]));
                    result.Add("Build warning file found at:" + errorFiles[ii]);
                }
#endif
                OldLogger.Stop("generateErrorReport - " + client.Path);
                cache.Add(client.Name, result.ToArray());
            }
            Debug.Assert(cache.ContainsKey(client.Name));
            return cache[client.Name];
        }
    }
}
