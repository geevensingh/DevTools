namespace JsonViewer.Model
{
    using System.Collections.Generic;

    public class DeserializeResult
    {
        private SortedDictionary<string, object> _dictionary;
        private string _preJsonText = string.Empty;
        private string _postJsonText = string.Empty;

        public DeserializeResult(Dictionary<string, object> dictionary)
        {
            _dictionary = new SortedDictionary<string, object>(dictionary, new DictionaryComparer());
        }

        public DeserializeResult(Dictionary<string, object> dictionary, string preJsonText, string postJsonText)
        {
            _dictionary = new SortedDictionary<string, object>(dictionary, new DictionaryComparer());
            _preJsonText = preJsonText;
            _postJsonText = postJsonText;
        }

        public SortedDictionary<string, object> Dictionary { get => _dictionary; }

        public string PreJsonText { get => _preJsonText; }

        public string PostJsonText { get => _postJsonText; }

        public bool HasExtraText { get => !string.IsNullOrEmpty(this.PreJsonText) || !string.IsNullOrEmpty(this.PostJsonText); }

        public SortedDictionary<string, object> GetEverythingDictionary()
        {
            SortedDictionary<string, object> dict = this.Dictionary;
            if (this.HasExtraText)
            {
                dict = new SortedDictionary<string, object>(new DictionaryComparer());
                if (!string.IsNullOrEmpty(this.PreJsonText))
                {
                    dict[DictionaryComparer.PreJsonKey] = this.PreJsonText;
                }

                foreach (string key in this.Dictionary.Keys)
                {
                    dict[key] = this.Dictionary[key];
                }

                if (!string.IsNullOrEmpty(this.PostJsonText))
                {
                    dict[DictionaryComparer.PostJsonKey] = this.PostJsonText;
                }
            }

            return dict;
        }
    }

    internal class DictionaryComparer : IComparer<string>
    {
        public const string PreJsonKey = "Pre-JSON text";
        public const string PostJsonKey = "Post-JSON text";

        public int Compare(string x, string y)
        {
            if (x == y)
            {
                return 0;
            }

            switch (x)
            {
                case PreJsonKey:
                    return -1;
                case PostJsonKey:
                    return 1;
            }

            switch (y)
            {
                case PreJsonKey:
                    return 1;
                case PostJsonKey:
                    return -1;
            }

            return x.CompareTo(y);
        }
    }

    internal static class DeserializeResultExtensions
    {
        public static bool IsSuccessful(this DeserializeResult deserializeResult)
        {
            return deserializeResult?.Dictionary != null;
        }
    }
}
