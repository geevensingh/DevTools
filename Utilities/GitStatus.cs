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

        private void ParseLocalChangeLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            Debug.Assert(line[2] == ' ');
            string fileName = GetFileName(line);

            if (line.StartsWith("?? "))
            {
                m_unstagedAdded.Add(fileName);
                return;
            }

            switch (line[0])
            {
                case 'A':
                    m_stagedAdded.Add(fileName);
                    break;
                case 'D':
                    m_stagedDeleted.Add(fileName);
                    break;
                case 'R':   // Rename
                case 'M':   // Modified
                case 'U':   // Merge conflict
                    m_stagedModified.Add(fileName);
                    break;
                case ' ':
                    break;
                default:
                    throw new Exception("Unexpected status.  Line: " + line);
            }
            switch (line[1])
            {
                case 'A':
                    m_unstagedAdded.Add(fileName);
                    break;
                case 'D':
                    m_unstagedDeleted.Add(fileName);
                    break;
                case 'R':   // Rename
                case 'M':   // Modified
                case 'U':   // Merge conflict
                    m_unstagedModified.Add(fileName);
                    break;
                case ' ':
                    break;
                default:
                    throw new Exception("Unexpected status.  Line: " + line);
            }
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
                return "[ " +
                    "+" + StagedAdded + " " +
                    "~" + StagedModified + " " +
                    "-" + StagedDeleted + " | " +
                    "+" + UnstagedAdded + " " +
                    "~" + UnstagedModified + " " +
                    "-" + UnstagedDeleted + " ! ]";
            }
        }

        public string MimimalLocalChanges
        {
            get
            {
                List<string> strings = new List<string>();
                if (StagedAdded + StagedModified + StagedDeleted != 0)
                {
                    strings.Add(string.Join(" ", new string[] { "+" + StagedAdded, "~" + StagedModified, "-" + StagedDeleted }));
                }
                if (UnstagedAdded + UnstagedModified + UnstagedDeleted != 0)
                {
                    strings.Add(string.Join(" ", new string[] { "+" + UnstagedAdded, "~" + UnstagedModified, "-" + UnstagedDeleted, "!" }));
                }
                if (strings.Count == 0)
                {
                    return string.Empty;
                }

                if (strings.Count == 2)
                {
                    strings.Insert(1, "|");
                }

                strings.Insert(0, "[");
                strings.Add("]");
                return string.Join(" ", strings.ToArray());
            }
        }

        private static string GetFileName(string line)
        {
            // TODO: get the filename out of the line
            return line;
        }

        private bool m_remoteGone = false;
        private string m_upstreamName;
        private int m_aheadCount;
        private int m_behindCount;

        public string Branch
        {
            get { return m_branch; }
        }
        public int StagedAdded
        {
            get { return m_stagedAdded.Count; }
        }
        public int StagedModified
        {
            get { return m_stagedModified.Count; }
        }
        public int StagedDeleted
        {
            get { return m_stagedDeleted.Count; }
        }
        public int UnstagedAdded
        {
            get { return m_unstagedAdded.Count; }
        }
        public int UnstagedModified
        {
            get { return m_unstagedModified.Count; }
        }
        public int UnstagedDeleted
        {
            get { return m_unstagedDeleted.Count; }
        }

        public bool AnyChanges
        {
            get
            {
                return StagedAdded + StagedModified + StagedDeleted + UnstagedAdded + UnstagedModified + UnstagedDeleted > 0;
            }
        }

        private string m_branch = null;
        private List<string> m_stagedAdded = new List<string>();
        private List<string> m_stagedModified = new List<string>();
        private List<string> m_stagedDeleted = new List<string>();
        private List<string> m_unstagedAdded = new List<string>();
        private List<string> m_unstagedModified = new List<string>();
        private List<string> m_unstagedDeleted = new List<string>();
    }
}
