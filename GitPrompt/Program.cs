﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utilities;

namespace GitPrompt
{
    class Program
    {
        static void Main(string[] args)
        {
            Logger.AnnounceStartStopActions = false;
#if DEBUG
            Logger.AnnounceStartStopActions = true;
#endif

            GitStatus status = GitStatus.Get();
            string root = Environment.GetEnvironmentVariable("REPO_ROOT");
            Logger.LogLine(String.Join(" ", new string[] { root, status.Branch, status.RemoteChanges, status.AllLocalChanges }));
        }
    }
}