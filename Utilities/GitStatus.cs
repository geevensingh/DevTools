using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Utilities-Tests")]
namespace Utilities
{
    public class GitStatus
    {
        public static GitStatus GetStatus()
        {
            ProcessHelper proc = new ProcessHelper("git.exe", "status");
            return ParseLines(proc.Go());
        }

        private GitStatus()
        {
        }

        static internal GitStatus ParseLines(string[] lines)
        {
            GitStatus that = new GitStatus();
            bool staged = true;
            for (int ii = 0; ii < lines.Length; ii++)
            {
                string line = lines[ii].Trim();
                if (line == "Changes not staged for commit:")
                {
                    staged = false;
                }
                else if (line == "Untracked files:")
                {
                    for (int jj = ii + 2; jj < lines.Length; jj++)
                    {
                        string innerLine = lines[jj];
                        if (innerLine != innerLine.TrimStart() && !innerLine.Trim().StartsWith("("))
                        {
                            string filename = GetFileName(innerLine.Trim());
                            if (!string.IsNullOrEmpty(filename))
                            {
                                that.m_unstagedAdded.Add(filename);
                            }
                        }
                    }
                    break;
                }
                else
                {
                    List<string> list = null;
                    if (line.StartsWith("new file:"))
                    {
                        if (staged)
                        {
                            list = that.m_stagedAdded;
                        }
                        else
                        {
                            list = that.m_unstagedAdded;
                        }
                    }
                    else if (line.StartsWith("modified:"))
                    {
                        if (staged)
                        {
                            list = that.m_stagedModified;
                        }
                        else
                        {
                            list = that.m_unstagedModified;
                        }
                    }
                    else if(line.StartsWith("deleted:"))
                    {
                        if (staged)
                        {
                            list = that.m_stagedDeleted;
                        }
                        else
                        {
                            list = that.m_unstagedDeleted;
                        }
                    }

                    if (list != null)
                    {
                        list.Add(GetFileName(line));
                    }
                }
            }
            return that;
        }

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

        public override string ToString()
        {
            return "[ " +
                "+" + StagedAdded + " " +
                "~" + StagedModified + " " +
                "-" + StagedDeleted + " | " +
                "+" + UnstagedAdded + " " +
                "~" + UnstagedModified + " " +
                "-" + UnstagedDeleted + " ]";
        }

        private static string GetFileName(string line)
        {
            // TODO: get the filename out of the line
            return line;
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

        public bool AnyChanged
        {
            get
            {
                return StagedAdded + StagedModified + StagedDeleted + UnstagedAdded + UnstagedModified + UnstagedDeleted > 0;
            }
        }

        private List<string> m_stagedAdded = new List<string>();
        private List<string> m_stagedModified = new List<string>();
        private List<string> m_stagedDeleted = new List<string>();
        private List<string> m_unstagedAdded = new List<string>();
        private List<string> m_unstagedModified = new List<string>();
        private List<string> m_unstagedDeleted = new List<string>();
    }
}
