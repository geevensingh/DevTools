using System.IO;

namespace Utilities
{
    public enum FileState
    {
        Added,
        Modified,
        Deleted,
        Critical
    }

    public class GitFileInfo
    {
        public GitFileInfo(string filePath, FileState fileState, bool staged)
        {
            m_filePath = filePath;
            m_fileState = fileState;
            m_staged = staged;
        }
        private string m_filePath;
        private FileState m_fileState;
        private bool m_staged;

        public string FileName { get => Path.GetFileName(m_filePath); }

        public string FilePath { get => m_filePath; }
        public FileState FileState { get => m_fileState; }
        public bool Staged { get => m_staged; }
    }
}
