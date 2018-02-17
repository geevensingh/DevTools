namespace JsonViewer
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using Microsoft.Win32;
    using Utilities;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private Finder _finder;
        private Point? _initialOffset = null;

        public MainWindow()
        {
            _finder = new Finder(this);
            _finder.PropertyChanged += OnFinderPropertyChanged;

            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;

            InitializeComponent();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public Point InitialOffset { set => _initialOffset = value; }

        public Visibility ToolbarTextVisibility { get => Properties.Settings.Default.MainWindowToolbarTextVisible ? Visibility.Visible : Visibility.Collapsed; }

        public Visibility ToolbarIconVisibility { get => Properties.Settings.Default.MainWindowToolbarIconVisible ? Visibility.Visible : Visibility.Collapsed; }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowPlacementSerializer.SetPlacement(this, Properties.Settings.Default.MainWindowPlacement, _initialOffset);
            if (_initialOffset.HasValue)
            {
                this.SaveWindowPosition();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            this.SaveWindowPosition();
            base.OnClosing(e);
        }

        private void SaveWindowPosition()
        {
            Properties.Settings.Default.MainWindowPlacement = WindowPlacementSerializer.GetPlacement(this);
            Properties.Settings.Default.Save();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            App app = (App)App.Current;
            string initialText = app.InitialText;
            app.InitialText = string.Empty;

            if (string.IsNullOrEmpty(initialText) && Clipboard.ContainsText())
            {
                string jsonString = Clipboard.GetText();
                if (JsonObjectFactory.TryDeserialize(jsonString) != null)
                {
                    initialText = jsonString;
                }
            }

            this.Raw_TextBox.Text = initialText;

            FindTextBox.Text = _finder.Text;

            Config config = Config.This;
            this.Tree.Foreground = config.GetBrush(ConfigValue.TreeViewForeground);
            this.Tree.Resources[SystemColors.HighlightBrushKey] = config.GetBrush(ConfigValue.TreeViewHighlightBrushKey);
            this.Tree.Resources[SystemColors.HighlightTextBrushKey] = config.GetBrush(ConfigValue.TreeViewHighlightTextBrushKey);
            this.Tree.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = config.GetBrush(ConfigValue.TreeViewInactiveSelectionHighlightBrushKey);
            this.Tree.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = config.GetBrush(ConfigValue.TreeViewInactiveSelectionHighlightTextBrushKey);
            this.HighlightParentsButton.IsChecked = Properties.Settings.Default.HighlightSelectedParents;
        }

        private void OnSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "MainWindowToolbarTextVisible":
                    NotifyPropertyChanged.FirePropertyChanged("ToolbarTextVisibility", this, this.PropertyChanged);
                    break;
                case "MainWindowToolbarIconVisible":
                    NotifyPropertyChanged.FirePropertyChanged("ToolbarIconVisibility", this, this.PropertyChanged);
                    break;
            }
        }

        private void OnFinderPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Debug.Assert(sender == _finder);
            if (e.PropertyName == "Text")
            {
                this.FindTextBox.Text = _finder.Text;
            }
        }

        private void SetErrorMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                this.Banner.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.Banner.Text = message;
                this.Banner.Visibility = Visibility.Visible;
            }
        }

        private async void Raw_TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Debug.Assert(sender.Equals(this.Raw_TextBox));
            bool succeeded = await this.ReloadAsync();
            if (!succeeded)
            {
                // Fix json copied from iScope scripts
                string text = this.Raw_TextBox.Text.Trim();
                if (text.StartsWith("\"") && text.EndsWith("\""))
                {
                    text = text.Trim(new char[] { '"' });
                    text = text.Replace("\"\"", "\"");
                    this.Raw_TextBox.Text = text;
                }
            }
        }

        private async Task<bool> ReloadAsync()
        {
            JsonObjectFactory factory = new JsonObjectFactory();
            RootObject rootObject = await factory.Parse(this.Raw_TextBox.Text);
            _finder.SetObjects(rootObject);
            if (rootObject == null)
            {
                this.SetErrorMessage("Unable to parse given string");
                return false;
            }

            this.SetErrorMessage(string.Empty);
            this.Tree.ItemsSource = rootObject.ViewChildren;

            if (rootObject.TotalChildCount <= 50)
            {
                this.Tree.ExpandAll();
            }

            return true;
        }

        private void ContextExpandChildren_Click(object sender, RoutedEventArgs e)
        {
            Debug.Assert(sender as FrameworkElement == sender);
            FrameworkElement element = sender as FrameworkElement;
            Debug.Assert(element.DataContext.GetType() == typeof(TreeViewData));
            this.Tree.ExpandChildren(element.DataContext as TreeViewData);
        }

        private void ContextExpandAll_Click(object sender, RoutedEventArgs e)
        {
            Debug.Assert(sender as FrameworkElement == sender);
            FrameworkElement element = sender as FrameworkElement;
            Debug.Assert(element.DataContext.GetType() == typeof(TreeViewData));
            this.Tree.ExpandSubtree(element.DataContext as TreeViewData);
        }

        private void ContextCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            Debug.Assert(sender as FrameworkElement == sender);
            FrameworkElement element = sender as FrameworkElement;
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

        private void ContextCopyValue_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(((sender as FrameworkElement).DataContext as TreeViewData).Value);
        }

        private void ContextCopyEscapedValue_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(CSEscape.Escape(((sender as FrameworkElement).DataContext as TreeViewData).Value));
        }

        private void Tree_CommandBinding_Copy(object sender, ExecutedRoutedEventArgs e)
        {
            TreeViewData selectedData = this.Tree.SelectedItem as TreeViewData;
            if (selectedData != null)
            {
                Clipboard.SetText(selectedData.Value);
            }
        }

        private void Tree_CommandBinding_Find(object sender, ExecutedRoutedEventArgs e)
        {
            _finder.ShowWindow();
        }

        private void Tree_CommandBinding_HideFind(object sender, ExecutedRoutedEventArgs e)
        {
            CommandFactory.HideFind_Execute(_finder);
        }

        private void Reload_CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Config.Reload();
            this.ReloadAsync().Forget();
        }

        private void NewWindow_CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            this.SaveWindowPosition();

            MainWindow newWindow = new MainWindow();
            newWindow.InitialOffset = new Point(20, 20);
            newWindow.Show();
        }

        private void ContextTreatAsJson_Click(object sender, RoutedEventArgs e)
        {
            ((sender as FrameworkElement).DataContext as TreeViewData).TreatAsJson();
        }

        private void ContextTreatAsText_Click(object sender, RoutedEventArgs e)
        {
            ((sender as FrameworkElement).DataContext as TreeViewData).TreatAsText();
        }

        private void PickConfig_CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            string filePath = this.PickJsonFile();
            if (!string.IsNullOrEmpty(filePath))
            {
                if (Config.SetPath(filePath))
                {
                    this.ReloadAsync().Forget();
                }
                else
                {
                    MessageBox.Show(this, "Unable to load config: " + filePath, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenJsonFile_CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            string filePath = this.PickJsonFile();
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    this.Raw_TextBox.Text = System.IO.File.ReadAllText(filePath);
                }
                catch
                {
                }
            }
        }

        private string PickJsonFile()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Json files (*.json)|*.json|All files (*.*)|*.*"
            };
            bool? ofdResult = openFileDialog.ShowDialog(this);
            if (ofdResult.HasValue && ofdResult.Value)
            {
                return openFileDialog.FileName;
            }

            return null;
        }

        private void HighlightParents_CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Properties.Settings.Default.HighlightSelectedParents = !Properties.Settings.Default.HighlightSelectedParents;
            Properties.Settings.Default.Save();
            this.HighlightParentsButton.IsChecked = Properties.Settings.Default.HighlightSelectedParents;
            TreeViewData selected = Tree.SelectedValue as TreeViewData;
            if (selected != null)
            {
                selected.IsSelected = selected.IsSelected;
            }
        }

        private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _finder.Text = FindTextBox.Text;
        }

        static int foo = 0;
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            switch(foo++ % 3)
            {
                case 0:
                    Properties.Settings.Default.MainWindowToolbarTextVisible = true;
                    Properties.Settings.Default.MainWindowToolbarIconVisible = true;
                    break;
                case 1:
                    Properties.Settings.Default.MainWindowToolbarTextVisible = true;
                    Properties.Settings.Default.MainWindowToolbarIconVisible = false;
                    break;
                case 2:
                    Properties.Settings.Default.MainWindowToolbarTextVisible = false;
                    Properties.Settings.Default.MainWindowToolbarIconVisible = true;
                    break;
            }
            Properties.Settings.Default.Save();
        }
    }
}
