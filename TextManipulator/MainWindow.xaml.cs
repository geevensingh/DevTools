using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TextManipulator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static System.Threading.Timer _timer;
        public MainWindow()
        {
            InitializeComponent();
            var dispatcherTimer = new System.Windows.Threading.DispatcherTimer();
            dispatcherTimer.Tick += new EventHandler((object o, EventArgs ea) =>
            {
                dispatcherTimer.Stop();
                this.Raw_TextBox.Text = System.IO.File.ReadAllText(@"S:\Repos\DevTools\TextManipulator\Test.json");
            });
            dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
            dispatcherTimer.Start();
        }

        private void Raw_TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Debug.Assert(sender.Equals(this.Raw_TextBox));
            System.Web.Script.Serialization.JavaScriptSerializer ser = new System.Web.Script.Serialization.JavaScriptSerializer();
            Dictionary<string, object> jsonObj = ser.Deserialize<Dictionary<string, object>>(this.Raw_TextBox.Text);
            //StringBuilder sb = new StringBuilder();
            //Stringify(0, jsonObj, ref sb);
            //this.Pretty_TextBox.Text = Prettyify(sb.ToString());
            this.Pretty_TextBox.Text = Prettyify(ser.Serialize(jsonObj));
            Treeify(this.Tree.Items, jsonObj);
        }

        private void Treeify(ItemCollection items, Dictionary<string, object> dictionary)
        {
            foreach(string key in dictionary.Keys)
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
            for(int ii = 0; ii < arrayList.Count; ii++)
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
            for(int ii = 0; ii < lines.Length; ii++)
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
