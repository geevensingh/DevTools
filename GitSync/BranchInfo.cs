namespace GitSync
{
    using Utilities;

    class BranchInfo
    {
        public BranchInfo(string name)
        {
            this.Name = name;
            this.ParentBranchName = GitOperations.GetBranchBase(name);
        }

        public string Name
        {
            get;
        }

        public string ParentBranchName
        {
            get;
        }

        public bool IsParented => !string.IsNullOrEmpty(this.ParentBranchName);

        public bool HasRemoteBranch
        {
            get;
            internal set;
        }

        public bool IsDefault
        {
            get;
            internal set;
        }
        public bool IsDeleted
        {
            get;
            internal set;
        }
    }
}
