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
    }
}
