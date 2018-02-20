namespace JsonViewer
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Finder _finder;
        private Point? _initialOffset = null;
        private RootObject _rootObject = null;

        public MainWindow()
        {
            _finder = new Finder(this);

            InitializeComponent();

            App.Current.AddWindow(this);
        }

        public Finder Finder { get => _finder; }

        internal RootObject RootObject { get => _rootObject; }

        public void ShowNewWindow()
        {
            this.SaveWindowPosition();

            MainWindow newWindow = new MainWindow
            {
                _initialOffset = new Point(20, 20)
            };
            newWindow.Show();
        }

        public async Task<bool> ReloadAsync()
        {
            JsonObjectFactory factory = new JsonObjectFactory();
            RootObject rootObject = await factory.Parse(this.Raw_TextBox.Text);
            _finder.SetObjects(rootObject);
            if (rootObject == null)
            {
                this.SetErrorMessage("Unable to parse given string");
                return false;
            }

            _rootObject = rootObject;
            this.SetErrorMessage(string.Empty);
            this.Tree.ItemsSource = _rootObject.ViewChildren;
            CommandFactory.ExpandAll.Update();
            CommandFactory.CollapseAll.Update();

            if (_rootObject.TotalChildCount <= 50)
            {
                this.Tree.ExpandAll();
            }

            return true;
        }

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
            App.Current.RemoveWindow(this);
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

        private void ContextExpandChildren_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            Debug.Assert(element == sender);
            Debug.Assert(element.DataContext.GetType() == typeof(TreeViewData));
            this.Tree.ExpandChildren(element.DataContext as TreeViewData);
        }

        private void ContextExpandAll_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            Debug.Assert(element == sender);
            Debug.Assert(element.DataContext.GetType() == typeof(TreeViewData));
            this.Tree.ExpandSubtree(element.DataContext as TreeViewData);
        }

        private void ContextCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            Debug.Assert(element == sender);
            Debug.Assert(element.DataContext.GetType() == typeof(TreeViewData));
            this.Tree.CollapseSubtree(element.DataContext as TreeViewData);
        }

        private void ContextCopyKey_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(((sender as FrameworkElement).DataContext as TreeViewData).KeyName);
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
            if (this.Tree.SelectedItem is TreeViewData selectedData)
            {
                Clipboard.SetText(selectedData.Value);
            }
        }

        private void CommandBinding_Find(object sender, ExecutedRoutedEventArgs e)
        {
            _finder.ShowWindow();
        }

        private void ContextTreatAsJson_Click(object sender, RoutedEventArgs e)
        {
            ((sender as FrameworkElement).DataContext as TreeViewData).TreatAsJson();
        }

        private void ContextTreatAsText_Click(object sender, RoutedEventArgs e)
        {
            ((sender as FrameworkElement).DataContext as TreeViewData).TreatAsText();
        }
    }
}
