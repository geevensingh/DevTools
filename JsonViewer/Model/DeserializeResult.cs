namespace JsonViewer.Model
{
    using System.Collections.Generic;

    public class DeserializeResult
    {
        private Dictionary<string, object> _dictionary;
        private string _preJsonText = string.Empty;
        private string _postJsonText = string.Empty;

        public DeserializeResult(Dictionary<string, object> dictionary)
        {
            _dictionary = dictionary;
        }

        public DeserializeResult(Dictionary<string, object> dictionary, string preJsonText, string postJsonText)
        {
            _dictionary = dictionary;
            _preJsonText = preJsonText;
            _postJsonText = postJsonText;
        }

        public Dictionary<string, object> Dictionary { get => _dictionary; }

        public string PreJsonText { get => _preJsonText; }

        public string PostJsonText { get => _postJsonText; }

        public bool HasExtraText { get => !string.IsNullOrEmpty(this.PreJsonText) || !string.IsNullOrEmpty(this.PostJsonText); }

        public Dictionary<string, object> GetEverythingDictionary()
        {
            Dictionary<string, object> dict = this.Dictionary;
            if (this.HasExtraText)
            {
                dict = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(this.PreJsonText))
                {
                    dict["Pre-JSON text"] = this.PreJsonText;
                }

                foreach (string key in this.Dictionary.Keys)
                {
                    dict[key] = this.Dictionary[key];
                }

                if (!string.IsNullOrEmpty(this.PostJsonText))
                {
                    dict["Post-JSON text"] = this.PostJsonText;
                }
            }

            return dict;
        }
    }

    public static class DeserializeResultExtensions
    {
        public static bool IsSuccessful(this DeserializeResult deserializeResult)
        {
            return deserializeResult?.Dictionary != null;
        }
    }
}
