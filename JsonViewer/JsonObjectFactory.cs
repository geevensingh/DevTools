using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Diagnostics;

namespace JsonViewer
{
    class JsonObjectFactory : IDisposable
    {
        private CancellationTokenSource _refreshCancellationTokenSource = new CancellationTokenSource();

        public void Dispose()
        {
            _refreshCancellationTokenSource.Cancel();
            _refreshCancellationTokenSource.Dispose();
        }

        public async Task<IList<JsonObject>> Parse(string jsonString)
        {
            _refreshCancellationTokenSource.Cancel();
            _refreshCancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = _refreshCancellationTokenSource.Token;
            return await Task.Run<List<JsonObject>>(() =>
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

                //StringBuilder sb = new StringBuilder();
                //Stringify(0, jsonObj, ref sb);
                //this.Pretty_TextBox.Text = Prettyify(sb.ToString());
                //this.Pretty_TextBox.Text = Prettyify(ser.Serialize(jsonObj));
                //Treeify(this.Tree.Items, jsonObj);

                var jsonObjects = new List<JsonObject>();
                Flatten(ref jsonObjects, jsonObj, null);
                return jsonObjects;
            }, token);
        }
        public static Dictionary<string, object> TryDeserialize(string jsonString)
        {
            try
            {
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(jsonString);
            }
            catch (ArgumentException) { }
            try
            {
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(CSEscape.Unescape(jsonString));
            }
            catch (ArgumentException) { }
            return null;
        }

        public static void Flatten(ref List<JsonObject> items, Dictionary<string, object> dictionary, JsonObject parent)
        {
            foreach (string key in dictionary.Keys)
            {
                object jsonObject = dictionary[key];

                JsonObject data = new JsonObject(key, jsonObject, parent);
                if (parent == null)
                {
                    items.Add(data);
                }

                if (jsonObject != null)
                {
                    Type valueType = jsonObject.GetType();
                    if (valueType == typeof(Dictionary<string, object>))
                    {
                        Flatten(ref items, jsonObject as Dictionary<string, object>, data);
                    }
                    else if (valueType == typeof(System.Collections.ArrayList))
                    {
                        Flatten(ref items, jsonObject as System.Collections.ArrayList, data);
                    }
                }
            }
        }

        private static void Flatten(ref List<JsonObject> items, System.Collections.ArrayList arrayList, JsonObject parent)
        {
            for (int ii = 0; ii < arrayList.Count; ii++)
            {
                object jsonObject = arrayList[ii];

                JsonObject data = new JsonObject("[" + ii + "]", jsonObject, parent);
                if (parent == null)
                {
                    items.Add(data);
                }

                Type valueType = jsonObject.GetType();
                if (valueType == typeof(Dictionary<string, object>))
                {
                    Flatten(ref items, jsonObject as Dictionary<string, object>, data);
                }
                else if (valueType == typeof(System.Collections.ArrayList))
                {
                    Flatten(ref items, jsonObject as System.Collections.ArrayList, data);
                }
            }
        }


        private void Treeify(ItemCollection items, Dictionary<string, object> dictionary)
        {
            foreach (string key in dictionary.Keys)
            {
                TreeViewItem treeViewItem = new TreeViewItem();
                treeViewItem.Header = key;

                object jsonObject = dictionary[key];
                if (jsonObject != null)
                {
                    Type valueType = jsonObject.GetType();
                    if (valueType == typeof(Dictionary<string, object>))
                    {
                        Treeify(treeViewItem.Items, jsonObject as Dictionary<string, object>);
                    }
                    else if (valueType == typeof(System.Collections.ArrayList))
                    {
                        Treeify(treeViewItem.Items, jsonObject as System.Collections.ArrayList);
                    }
                }
                items.Add(treeViewItem);
            }
        }

        private void Treeify(ItemCollection items, System.Collections.ArrayList arrayList)
        {
            for (int ii = 0; ii < arrayList.Count; ii++)
            {
                TreeViewItem treeViewItem = new TreeViewItem();
                treeViewItem.Header = (ii + 1).ToString() + " / " + arrayList.Count.ToString();

                object jsonObject = arrayList[ii];
                Type valueType = jsonObject.GetType();
                if (valueType == typeof(Dictionary<string, object>))
                {
                    Treeify(treeViewItem.Items, jsonObject as Dictionary<string, object>);
                }
                else if (valueType == typeof(System.Collections.ArrayList))
                {
                    Treeify(treeViewItem.Items, jsonObject as System.Collections.ArrayList);
                }
                items.Add(treeViewItem);
            }
        }

        private void Stringify(int depth, Dictionary<string, object> jsonObject, ref StringBuilder sb)
        {
            sb.Append(Utilities.StringHelper.GeneratePrefix(depth));
            sb.Append("{");
            List<string> keys = new List<string>(jsonObject.Keys);
            for (int ii = 0; ii < keys.Count; ii++)
            {
                string key = keys[ii];
                sb.Append(Utilities.StringHelper.GeneratePrefix(depth) + key + " : ");
                Stringify(depth, jsonObject[key], ref sb);
                if (ii != keys.Count - 1)
                {
                    sb.Append(",");
                }
                sb.Append("\r\n");
            }
            sb.Append("}\r\n");
        }

        private void Stringify(int depth, object jsonObject, ref StringBuilder sb)
        {
            if (jsonObject == null)
            {
                sb.Append("null\r\n");
                return;
            }

            Type valueType = jsonObject.GetType();
            if (valueType == typeof(Dictionary<string, object>))
            {
                sb.Append("\r\n");
                Stringify(depth + 1, jsonObject as Dictionary<string, object>, ref sb);
            }
            else if (valueType == typeof(System.Collections.ArrayList))
            {
                var arrayList = jsonObject as System.Collections.ArrayList;
                sb.Append("[");
                foreach (object arrayObj in arrayList)
                {
                    Stringify(depth + 1, arrayObj, ref sb);
                }
                sb.Append("]");
            }
            else
            {
                sb.Append(jsonObject + "\r\n");
            }
        }

        private string Prettyify(string json)
        {
            json = new Regex("{").Replace(json, "{\r\n");
            json = new Regex("}").Replace(json, "}\r\n");
            json = new Regex(":{").Replace(json, ": {");
            json = new Regex("}\r\n").Replace(json, "\r\n}\r\n");

            json = new Regex("\\[").Replace(json, "[\r\n");
            json = new Regex("\\]").Replace(json, "]\r\n");
            json = new Regex(":\\[").Replace(json, ": [");
            json = new Regex("\\]\r\n").Replace(json, "\r\n]\r\n");


            json = new Regex(",").Replace(json, ",\r\n");
            json = new Regex("\r\n\"").Replace(json, "\r\n");
            json = new Regex("\":").Replace(json, " : ");
            json = new Regex("\r\n,").Replace(json, ",");
            string[] lines = json.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            int depth = 0;
            StringBuilder sb = new StringBuilder();
            for (int ii = 0; ii < lines.Length; ii++)
            {
                string line = lines[ii];
                if (line.Contains("}"))
                {
                    depth--;
                }
                if (line.Contains("]"))
                {
                    depth--;
                }

                sb.Append(Utilities.StringHelper.GeneratePrefix(depth, "  "));
                sb.Append(line);
                sb.Append("\r\n");

                if (line.Contains("{"))
                {
                    depth++;
                }
                if (line.Contains("["))
                {
                    depth++;
                }
            }
            return sb.ToString(); ;
        }
    }
}
