using System;
using System.Diagnostics;

namespace Utilities
{
    public class Client
    {
        private string _name;
        private string _path;
        private Client _parent = null;
        private Depot _depot = null;

        public Client Parent
        {
            get
            {
                return _parent;
            }
        }

        public Client Root
        {
            get
            {
                Client root = this;
                while (root.Parent != null)
                {
                    root = root.Parent;
                }
                return root;
            }
        }

        public string Name
        {
            get
            {
                Debug.Assert(_name.Length > 0);
                return _name;
            }
            set
            {
                Debug.Assert(value.Length > 0);
                _name = value;
            }
        }

        public bool IsLocal
        {
            get
            {
                return !String.IsNullOrEmpty(_path);
            }
        }

        public string Path
        {
            get
            {
                Debug.Assert(_path.Length > 0);
                return _path;
            }
            set
            {
                Debug.Assert(value.Length > 0);
                _path = System.IO.Directory.Exists(value) ? value : null;
            }
        }

        public string DisplayName
        {
            get
            {
                if (!IsLocal)
                {
                    return Name;
                }
                return Name + " ( " + Path + " )";
            }
        }

        public Depot Depot
        {
            get
            {
                Debug.Assert(_depot != null);
                return _depot;
            }
            set
            {
                Debug.Assert(_depot == null || _depot == value);
                _depot = value;
            }
        }
        public string DepotName
        {
            get
            {
                if (Parent == null)
                {
                    return "\\.";
                }
                Debug.Assert(Path.StartsWith(Parent.Path));
                return Path.Substring(Parent.Path.Length);
            }
        }

        public Client()
        {
        }

        public Client(Client parent)
        {
            _parent = parent;
        }

        public string[] getResolveIssues()
        {
#if false
            return new string[] { "resolve", "issues", "galore" };
#else
            return (new SDOperations()).GetResolveIssues(this);
#endif
        }

        public string[] getPossibleConflicts()
        {
#if false
            return new string[] { "possible", "conflicts", "galore" };
#else
            return (new SDOperations()).GetPossibleConflicts(this);
#endif
        }

        static public int Compare(Client a, Client b)
        {
            if (a.IsLocal != b.IsLocal)
            {
                return a.IsLocal ? -1 : 1;
            }
            return a.DisplayName.CompareTo(b.DisplayName);
        }
    }
}
