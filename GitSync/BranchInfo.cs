namespace GitSync
{
    using Utilities;

    class BranchInfo
    {
        public BranchInfo(string name)
        {
            this.Name = name;
            var parentBranchName = GitOperations.GetBranchBase(name);
            if (parentBranchName == "root")
            {
                this.IsRoot = true;
            }
            else
            {
                this.ParentBranchName = parentBranchName;
            }
        }

        public string Name
        {
            get;
        }

        public string ParentBranchName
        {
            get;
        }
        public bool IsRoot
        {
            get;
            internal set;
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
