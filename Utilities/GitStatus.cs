using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Utilities-Tests")]
namespace Utilities
{
    public class GitStatus
    {
        public static GitStatus Get()
        {
            ProcessHelper proc = new ProcessHelper("git.exe", "status -b --porcelain=v1");
            return ParseLines(proc.Go());
        }

        private GitStatus()
        {
        }

        static internal GitStatus ParseLines(string[] lines)
        {
            GitStatus that = new GitStatus();
            that.ParseBranchLine(lines[0]);
            for (int ii = 1; ii < lines.Length; ii++)
            {
                that.ParseLocalChangeLine(lines[ii]);
            }
            return that;
        }

        private void ParseFileLine(int index, bool staged, string line)
        {
            char ch = line[index];
            if (ch == ' ')
            {
                return;
            }

            if (GetFileName(line, out string filePath))
            {
                switch (ch)
                {
                    case 'A':
                        m_files.Add(new GitFileInfo(filePath, FileState.Added, staged));
                        break;
                    case 'D':
                        m_files.Add(new GitFileInfo(filePath, FileState.Deleted, staged));
                        break;
                    case 'R':   // Rename
                    case 'M':   // Modified
                    case 'C':   // Copied
                        m_files.Add(new GitFileInfo(filePath, FileState.Modified, staged));
                        break;
                    case 'U':   // Merge conflict
                        m_files.Add(new GitFileInfo(filePath, FileState.Critical, staged));
                        break;
                    default:
                        throw new Exception("Unexpected status.  Line: " + line);
                }
            }
        }

        private void ParseLocalChangeLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            Debug.Assert(line[2] == ' ');
            
            if (line.StartsWith("?? ") && GetFileName(line, out string filePath))
            {
                m_files.Add(new GitFileInfo(filePath, FileState.Added, false));
                return;
            }

            ParseFileLine(0, true, line);
            ParseFileLine(1, false, line);
        }

        private void ParseBranchLine(string line)
        {
            Debug.Assert(line.StartsWith("## "));
            string[] splits = line.Split(new char[] { ' ' });
            Debug.Assert(splits[0] == "##");
            string[] branches = splits[1].Split(new string[] { "..." }, StringSplitOptions.None);
            Debug.Assert(branches.Length > 0);
            Debug.Assert(branches.Length <= 2);
            m_branch = branches[0];
            m_upstreamName = branches.Length > 1 ? branches[1] : string.Empty;

            string[] remoteChanges = line.Split(new char[] { '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
            Debug.Assert(remoteChanges.Length == 1 || remoteChanges.Length == 2);
            if (remoteChanges.Length == 2 && !string.IsNullOrEmpty(remoteChanges[1]))
            {
                if (remoteChanges[1] == "gone")
                {
                    m_remoteGone = true;
                }
                else
                {
                    string[] remoteChangeSplit = remoteChanges[1].Split(new char[] { ',' });
                    for (int ii = 0; ii < remoteChangeSplit.Length; ii++)
                    {
                        string[] subSplit = remoteChangeSplit[ii].Trim().Split(new char[] { ' ' });
                        Debug.Assert(subSplit.Length == 2);
                        int count = int.Parse(subSplit[1]);
                        if (subSplit[0] == "ahead")
                        {
                            m_aheadCount = count;
                        }
                        else if (subSplit[0] == "behind")
                        {
                            m_behindCount = count;
                        }
                        else
                        {
                            throw new Exception("Unknown branch status.  Line: " + line);
                        }
                    }
                }
            }
        }

        internal const string UpToDateString = "≡";
        //internal const string BehindString = "↓";
        //internal const string AheadString = "↑";
        //internal const string GoneString = "x";

        public void WriteToConsole()
        {
            ConsoleColor previousColor = Console.ForegroundColor;
            Console.Write("[ ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("+" + StagedAdded + " ");
            Console.Write("~" + StagedModified + " ");
            Console.Write("-" + StagedDeleted);
            Console.ForegroundColor = previousColor;
            Console.Write(" | ");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("+" + UnstagedAdded + " ");
            Console.Write("~" + UnstagedModified + " ");
            Console.Write("-" + UnstagedDeleted);
            Console.ForegroundColor = previousColor;
            Console.Write(" ]");
        }

        public string RemoteChanges
        {
            get
            {
                if (m_remoteGone)
                {
                    return "remote-gone";
                }

                if (string.IsNullOrEmpty(m_upstreamName))
                {
                    return "no-remote";
                }

                List<string> strings = new List<string>();
                if (m_aheadCount > 0)
                {
                    strings.Add(m_aheadCount + " ahead");
                }
                if (m_behindCount > 0)
                {
                    strings.Add(m_behindCount + " behind");
                }
                if (strings.Count > 0)
                {
                    return string.Join(" ", strings);
                }
                return UpToDateString;
            }
        }

        public string AllLocalChanges
        {
            get
            {
                List<string> strings = new List<string>();
                strings.Add("[");
                strings.Add("+" + StagedAdded);
                strings.Add("~" + StagedModified);
                strings.Add("-" + StagedDeleted);
                if (StagedCritical > 0)
                {
                    strings.Add("#" + StagedCritical);
                }
                strings.Add("|");
                strings.Add("+" + UnstagedAdded);
                strings.Add("~" + UnstagedModified);
                strings.Add("-" + UnstagedDeleted);
                if (UnstagedCritical > 0)
                {
                    strings.Add("#" + UnstagedCritical);
                }
                strings.Add("!");
                strings.Add("]");

                return string.Join(" ", strings);
            }
        }

        public string MimimalLocalChanges
        {
            get
            {
                if (!AnyChanges)
                {
                    return string.Empty;
                }
                List<string> strings = new List<string>();
                if (Staged != 0)
                {
                    List<string> subStrings = new List<string>();
                    subStrings.Add("+" + StagedAdded);
                    subStrings.Add("~" + StagedModified);
                    subStrings.Add("-" + StagedDeleted);
                    if (StagedCritical > 0)
                    {
                        subStrings.Add("#" + StagedCritical);
                    }
                    strings.Add(string.Join(" ", subStrings));
                }
                if (Unstaged != 0)
                {
                    List<string> subStrings = new List<string>();
                    subStrings.Add("+" + UnstagedAdded);
                    subStrings.Add("~" + UnstagedModified);
                    subStrings.Add("-" + UnstagedDeleted);
                    if (UnstagedCritical > 0)
                    {
                        subStrings.Add("#" + UnstagedCritical);
                    }
                    subStrings.Add("!");
                    strings.Add(string.Join(" ", subStrings));
                }
                Debug.Assert(strings.Count > 0);

                return "[ " + string.Join(" | ", strings.ToArray()) + " ]";
            }
        }

        private static bool GetFileName(string line, out string filepath)
        {
            int lastSpaceIndex = line.LastIndexOf(" ");
            if (lastSpaceIndex == -1)
            {
                filepath = null;
                return false;
            }

            line = line.Replace("\"", "");
            int lastSlashIndex = line.LastIndexOf("/");
            if (lastSlashIndex == -1)
            {
                lastSlashIndex = lastSpaceIndex;
            }

            string filename = line.Substring(lastSlashIndex + 1);
            filepath = System.IO.Path.Combine(Environment.CurrentDirectory, line.Substring(lastSpaceIndex + 1).Replace('/', '\\'));
            Debug.Assert(filename == System.IO.Path.GetFileName(filename));
            return true;
        }

        private bool m_remoteGone = false;
        private string m_upstreamName;
        private int m_aheadCount;
        private int m_behindCount;

        public string Branch
        {
            get { return m_branch; }
        }
        public int Staged
        {
            get { return GetFiles(true).Length; }
        }
        public int StagedAdded
        {
            get { return GetFiles(FileState.Added, true).Length; }
        }
        public int StagedModified
        {
            get { return GetFiles(FileState.Modified, true).Length; }
        }
        public int StagedDeleted
        {
            get { return GetFiles(FileState.Deleted, true).Length; }
        }
        public int StagedCritical
        {
            get { return GetFiles(FileState.Critical, true).Length; }
        }
        public int Unstaged
        {
            get { return GetFiles(false).Length; }
        }
        public int UnstagedAdded
        {
            get { return GetFiles(FileState.Added, false).Length; }
        }
        public int UnstagedModified
        {
            get { return GetFiles(FileState.Modified, false).Length; }
        }
        public int UnstagedDeleted
        {
            get { return GetFiles(FileState.Deleted, false).Length; }
        }
        public int UnstagedCritical
        {
            get { return GetFiles(FileState.Critical, false).Length; }
        }

        public bool AnyChanges
        {
            get
            {
                return m_files.Count > 0;
            }
        }

        private string m_branch = null;
        private List<GitFileInfo> m_files = new List<GitFileInfo>();

        public GitFileInfo[] GetFiles(bool staged)
        {
            List<GitFileInfo> results = new List<GitFileInfo>();
            foreach (GitFileInfo info in m_files)
            {
                if (info.Staged == staged)
                {
                    results.Add(info);
                }
            }
            return results.ToArray();
        }
        private GitFileInfo[] GetFiles(FileState fileState, bool staged)
        {
            List<GitFileInfo> results = new List<GitFileInfo>();
            foreach (GitFileInfo info in m_files)
            {
                if (info.FileState == fileState && info.Staged == staged)
                {
                    results.Add(info);
                }
            }
            return results.ToArray();
        }
    }
}
