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
        private static string _configPath = @"C:\Repos\DevTools\TextManipulator\Config.json";
        private Config _config = new Config(_configPath);
        private Finder _finder;
        FindWindow _findWindow = null;
        public MainWindow()
        {
            InitializeComponent();
            var dispatcherTimer = new System.Windows.Threading.DispatcherTimer();
            dispatcherTimer.Tick += new EventHandler((object o, EventArgs ea) =>
            {
                dispatcherTimer.Stop();
                this.Raw_TextBox.Text = System.IO.File.ReadAllText(@"C:\Repos\DevTools\TextManipulator\Test.json");
            });
            dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
            dispatcherTimer.Start();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _findWindow = new FindWindow(this);
            _finder = new Finder(_findWindow);

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
            JsonObjectFactory factory = new JsonObjectFactory();
            IList<JsonObject> jsonObjects = await factory.Parse(this.Raw_TextBox.Text);
            _finder.SetObjects(jsonObjects);
            if (jsonObjects != null)
            {
                this.Tree.ItemsSource = TreeViewDataFactory.CreateCollection(jsonObjects);
            }
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

        private void Tree_CommandBinding_Find(object sender, ExecutedRoutedEventArgs e)
        {
            _findWindow.Show();
        }

        private void Tree_CommandBinding_HideFind(object sender, ExecutedRoutedEventArgs e)
        {
            CommandFactory.HideFind_Execute(_findWindow);
        }

        private void Tree_CommandBinding_CanHideFind(object sender, CanExecuteRoutedEventArgs e)
        {
            CommandFactory.HideFind_CanExecute(_findWindow, ref e);
        }
    }
}
