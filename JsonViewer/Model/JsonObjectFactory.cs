namespace JsonViewer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Script.Serialization;
    using Utilities;

    internal class JsonObjectFactory : IDisposable
    {
        private CancellationTokenSource _refreshCancellationTokenSource = new CancellationTokenSource();

        public static Dictionary<string, object> TryDeserialize(string jsonString)
        {
            jsonString = jsonString.Trim();
            int startIndex = jsonString.IndexOf("{");
            int endIndex = jsonString.LastIndexOf("}");
            if (startIndex < 0 || endIndex < 0 || endIndex < startIndex)
            {
                return null;
            }

            jsonString = jsonString.Substring(startIndex, endIndex - startIndex + 1);
            Debug.Assert(jsonString.StartsWith("{"));
            Debug.Assert(jsonString.EndsWith("}"));
            try
            {
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(jsonString);
            }
            catch (SystemException)
            {
            }

            try
            {
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(CSEscape.Unescape(jsonString));
            }
            catch (SystemException)
            {
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
                    Type valueType = rawObject.GetType();
                    if (valueType == typeof(Dictionary<string, object>))
                    {
                        Flatten(ref items, rawObject as Dictionary<string, object>, data);
                    }
                    else if (valueType == typeof(System.Collections.ArrayList))
                    {
                        Flatten(ref items, rawObject as System.Collections.ArrayList, data);
                    }
                }
            }

            parent.AddChildren(children);
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

                Type valueType = rawObject.GetType();
                if (valueType == typeof(Dictionary<string, object>))
                {
                    Flatten(ref items, rawObject as Dictionary<string, object>, data);
                }
                else if (valueType == typeof(System.Collections.ArrayList))
                {
                    Flatten(ref items, rawObject as System.Collections.ArrayList, data);
                }
            }

            parent.AddChildren(children);
        }

        public void Dispose()
        {
            _refreshCancellationTokenSource.Cancel();
            _refreshCancellationTokenSource.Dispose();
        }

        public async Task<RootObject> Parse(string jsonString)
        {
            _refreshCancellationTokenSource.Cancel();
            _refreshCancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = _refreshCancellationTokenSource.Token;
            return await Task.Run(
                () =>
                {
                    System.Web.Script.Serialization.JavaScriptSerializer ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                    if (token.IsCancellationRequested)
                    {
                        return null;
                    }
                    Dictionary<string, object> jsonObj = TryDeserialize(jsonString);
                    if (token.IsCancellationRequested || jsonObj == null)
                    {
                        return null;
                    }

                    RootObject root = new RootObject();
                    var jsonObjects = new List<JsonObject>();
                    Flatten(ref jsonObjects, jsonObj, root);
                    return root;
                }, token);
        }
    }
}
