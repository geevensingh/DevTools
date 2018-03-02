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

            foreach (string str in new string[] { jsonString, CSEscape.Unescape(jsonString) })
            {
                Dictionary<string, object> result = TryStrictDeserialize(str);
                if (result != null)
                {
                    return result;
                }

                result = TryStrictDeserialize(StringHelper.GetTrimmedString(str, "{", "}"));
                if (result != null)
                {
                    return result;
                }

                result = TryArrayDeserialize(str);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static Dictionary<string, object> TryArrayDeserialize(string jsonString)
        {
            string arrayString = StringHelper.GetTrimmedString(jsonString, "[", "]");
            if (!string.IsNullOrEmpty(arrayString))
            {
                arrayString = "{ \"array\": " + arrayString + " }";
                Dictionary<string, object> result = TryStrictDeserialize(arrayString);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
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
    }
}
