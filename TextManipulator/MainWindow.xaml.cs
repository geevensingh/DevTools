using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Threading;

namespace TextManipulator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static string _configPath = @"S:\Repos\DevTools\TextManipulator\Config.json";
        private Config _config = new Config(_configPath);
        private CancellationTokenSource _refreshCancellationTokenSource = new CancellationTokenSource();
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

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            this.Width = Math.Min(this.Width, 1000);
            this.Height = Math.Min(this.Height, 750);

            this.Tree.Foreground = _config.GetBrush(ConfigValue.treeViewForeground);
            this.Tree.Resources[SystemColors.HighlightBrushKey] = _config.GetBrush(ConfigValue.treeViewHighlightBrushKey);
            this.Tree.Resources[SystemColors.HighlightTextBrushKey] = _config.GetBrush(ConfigValue.treeViewHighlightTextBrushKey);
            this.Tree.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = _config.GetBrush(ConfigValue.treeViewInactiveSelectionHighlightBrushKey);
            this.Tree.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = _config.GetBrush(ConfigValue.treeViewInactiveSelectionHighlightTextBrushKey);
        }

        private void Raw_TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Debug.Assert(sender.Equals(this.Raw_TextBox));
            this.ReloadAsync();
        }

        private async void ReloadAsync()
        {
            _refreshCancellationTokenSource.Cancel();
            _refreshCancellationTokenSource = new CancellationTokenSource();
            string text = this.Raw_TextBox.Text;
            CancellationToken token = _refreshCancellationTokenSource.Token;
            List<TreeViewData> nodeList = await Task.Run<List<TreeViewData>>(() =>
            {
                System.Web.Script.Serialization.JavaScriptSerializer ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                if (token.IsCancellationRequested)
                {
                    return null;
                }
                try
                {
                    Dictionary<string, object> jsonObj = ser.Deserialize<Dictionary<string, object>>(text);
                    if (token.IsCancellationRequested)
                    {
                        return null;
                    }

                    //StringBuilder sb = new StringBuilder();
                    //Stringify(0, jsonObj, ref sb);
                    //this.Pretty_TextBox.Text = Prettyify(sb.ToString());
                    //this.Pretty_TextBox.Text = Prettyify(ser.Serialize(jsonObj));
                    //Treeify(this.Tree.Items, jsonObj);

                    var backgroundList = new List<TreeViewData>();
                    Flatten(ref backgroundList, jsonObj, null);
                    if (token.IsCancellationRequested)
                    {
                        return null;
                    }
                    return backgroundList;
                }
                catch { }
                return null;
            }, token);
            if (nodeList != null)
            {
                this.Tree.ItemsSource = new ObservableCollection<TreeViewData>(nodeList);
            }
        }

        private static void Flatten(ref List<TreeViewData> items, Dictionary<string, object> dictionary, TreeViewData parent)
        {
            foreach (string key in dictionary.Keys)
            {
                object jsonObject = dictionary[key];

                TreeViewData data = new TreeViewData(key, jsonObject, parent);
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

        private static void Flatten(ref List<TreeViewData> items, System.Collections.ArrayList arrayList, TreeViewData parent)
        {
            for (int ii = 0; ii < arrayList.Count; ii++)
            {
                object jsonObject = arrayList[ii];

                TreeViewData data = new TreeViewData("[" + ii + "]", jsonObject, parent);
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

        private void ContextExpandAll_Click(object sender, RoutedEventArgs e)
        {
            Debug.Assert(sender as FrameworkElement == sender);
            FrameworkElement element = (sender as FrameworkElement);
            Debug.Assert(element.DataContext.GetType() == typeof(TreeViewData));
            this.Tree.ExpandSubtree(element.DataContext as TreeViewData);
        }

        private void ContextCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            Debug.Assert(sender as FrameworkElement == sender);
            FrameworkElement element = (sender as FrameworkElement);
            Debug.Assert(element.DataContext.GetType() == typeof(TreeViewData));
            this.Tree.CollapseSubtree(element.DataContext as TreeViewData);
        }

        private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
        {
            this.Tree.ExpandAll();
        }
        private void CollapseAllButton_Click(object sender, RoutedEventArgs e)
        {
            this.Tree.CollapseAll();
        }

        private void CopyValue_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(((sender as FrameworkElement).DataContext as TreeViewData).Value);
        }

        private void Tree_CommandBinding_Copy(object sender, ExecutedRoutedEventArgs e)
        {
            TreeViewData selectedData = this.Tree.SelectedItem as TreeViewData;
            if (selectedData != null)
            {
                Clipboard.SetText(selectedData.Value);
            }
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            _config = Config.Reload(_configPath);
            this.ReloadAsync();
        }
    }
}
