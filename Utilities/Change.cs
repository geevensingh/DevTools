using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Utilities
{
    public class Change
    {
        private string _number;
        private Client _client;
        private string _shortDescription;
        private List<string> _longDescription;
        private List<string> _fileList;

        public string Number
        {
            get { return _number; }
            set
            {
#if DEBUG
                long temp;
                Debug.Assert((value == "default") || Int64.TryParse(value, out temp));
#endif
                _number = value;
            }
        }
        public Client Client
        {
            get { return _client; }
            set { _client = value; }
        }
        public string ShortDescription
        {
            get
            {
                if (_longDescription.Count > 0)
                {
                    string firstLine = _longDescription[0].Trim();
                    if (!string.IsNullOrEmpty(firstLine))
                    {
                        return Number + " : " + firstLine;
                    }
                }
                return _shortDescription;
            }
            set { _shortDescription = value; }
        }
        public List<string> LongDescription
        {
            get { return _longDescription; }
            set { _longDescription = value; }
        }
        public List<string> FileList
        {
            get { return _fileList; }
            set
            {
                _fileList = value;
                for (int ii = 0; ii < _fileList.Count; ii++)
                {
                    var pieces = _fileList[ii].Split(new char[] {'/'}, StringSplitOptions.None);
                    for (int jj = 0; jj < pieces.Length; jj++)
                    {
                        if (pieces[jj].ToUpper() == pieces[jj])
                        {
                            pieces[jj] = pieces[jj].ToLower();
                        }
                    }
                    _fileList[ii] = String.Join("/", pieces);
                }
            }
        }

        public static int Compare(Change a, Change b)
        {
            var result = 0;
            try
            {
                result = a.Client.DisplayName.CompareTo(b.Client.DisplayName);
            }
            catch (Exception) { }
            if (result == 0)
            {
                result = a.Client.Name.CompareTo(b.Client.Name);
            }
            if (result == 0)
            {
                result = a.Client.Path.CompareTo(b.Client.Path);
            }
            if (result == 0)
            {
                bool aIsDefault = (a.Number == "default");
                bool bIsDefault = (b.Number == "default");
                result = bIsDefault.CompareTo(aIsDefault);
                if (result == 0)
                {
                    result = a.Number.CompareTo(b.Number);
                }
            }
            return result;
        }
    }
}
