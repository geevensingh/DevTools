namespace JsonViewer.Model
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Web.Script.Serialization;
    using Utilities;

    internal class JsonObjectFactory : IDisposable
    {
        private CancellationTokenSource _refreshCancellationTokenSource = new CancellationTokenSource();

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

        public static DeserializeResult TryDeserialize(string jsonString)
        {
            jsonString = jsonString.Trim();

            int firstBrace = jsonString.IndexOfAny(new char[] { '{', '[' });
            if (firstBrace < 0)
            {
                return null;
            }

            bool firstBraceIsSquare = jsonString[firstBrace] == '[';

            foreach (string str in new string[] { jsonString, CSEscape.Unescape(jsonString) })
            {
                Dictionary<string, object> dictionary = TryStrictDeserialize(str);
                if (dictionary != null)
                {
                    return new DeserializeResult(dictionary);
                }

                DeserializeResult result = null;
                if (firstBraceIsSquare)
                {
                    result = TryTrimmedDeserialize(str, "[", "]", "{{ \"array\": {0} }}");
                    if (result != null)
                    {
                        return result;
                    }
                }

                result = TryTrimmedDeserialize(str, "{", "}", "{0}");
                if (result != null)
                {
                    return result;
                }

                if (!firstBraceIsSquare)
                {
                    result = TryTrimmedDeserialize(str, "[", "]", "{{ \"array\": {0} }}");
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return null;
        }

        public static void Flatten(ref List<JsonObject> items, Dictionary<string, object> dictionary, JsonObject parent)
        {
            List<JsonObject> children = new List<JsonObject>();
            foreach (string key in dictionary.Keys)
            {
                object rawObject = dictionary[key];

                JsonObject data = new JsonObject(key, rawObject, parent);
                children.Add(data);

                if (parent == null)
                {
                    items.Add(data);
                }

                if (rawObject != null)
                {
                    if (rawObject is Dictionary<string, object>)
                    {
                        Flatten(ref items, rawObject as Dictionary<string, object>, data);
                    }
                    else if (rawObject is System.Collections.ArrayList)
                    {
                        Flatten(ref items, rawObject as System.Collections.ArrayList, data);
                    }
                }
            }

            parent.SetChildren(children);
        }

        public static void Flatten(ref List<JsonObject> items, System.Collections.ArrayList arrayList, JsonObject parent)
        {
            List<JsonObject> children = new List<JsonObject>();
            for (int ii = 0; ii < arrayList.Count; ii++)
            {
                object rawObject = arrayList[ii];

                JsonObject data = new JsonObject("[" + ii + "]", rawObject, parent);
                children.Add(data);

                if (parent == null)
                {
                    items.Add(data);
                }

                if (rawObject is Dictionary<string, object>)
                {
                    Flatten(ref items, rawObject as Dictionary<string, object>, data);
                }
                else if (rawObject is System.Collections.ArrayList)
                {
                    Flatten(ref items, rawObject as System.Collections.ArrayList, data);
                }
            }

            parent.SetChildren(children);
        }

        public void Dispose()
        {
            _refreshCancellationTokenSource.Cancel();
            _refreshCancellationTokenSource.Dispose();
        }

        private static DeserializeResult TryTrimmedDeserialize(string jsonString, string start, string end, string format)
        {
            IList<string> parts = StringHelper.SplitString(jsonString, start, end);
            if (parts == null)
            {
                return null;
            }

            Debug.Assert(parts.Count == 3);
            Debug.Assert(!string.IsNullOrEmpty(parts[1]));
            string trimmedString = string.Format(format, parts[1]);
            Dictionary<string, object> result = TryStrictDeserialize(trimmedString);
            if (result == null)
            {
                return null;
            }

            return new DeserializeResult(result, parts[0].Trim(), parts[2].Trim());
        }

        private static Dictionary<string, object> TryStrictDeserialize(string jsonString)
        {
            try
            {
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(jsonString);
            }
            catch (SystemException)
            {
            }

            return null;
        }
    }
}
