namespace JsonViewer
{
    using System;
    using System.ComponentModel;
    using System.Deployment.Application;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private Finder _finder;
        private Point? _initialOffset = null;
        private RootObject _rootObject = null;
        private WarningBannerActionHandler _warningBannerAction;
        private WarningBannerActionHandler _warningBannerDismiss;

        public MainWindow()
        {
            _finder = new Finder(this);

            InitializeComponent();
        }

        private delegate void WarningBannerActionHandler();

        public event PropertyChangedEventHandler PropertyChanged;

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
            if (string.IsNullOrWhiteSpace(this.Raw_TextBox.Text))
            {
                return false;
            }

            JsonObjectFactory factory = new JsonObjectFactory();
            RootObject rootObject = await factory.Parse(this.Raw_TextBox.Text);
            if (rootObject == null)
            {
                this.SetErrorMessage("Unable to parse given string");
                return false;
            }

            this.SetErrorMessage(string.Empty);
            int? oldSelectionIndex = this.Tree.SelectedIndex;
            TreeViewData treeViewData = (TreeViewData)this.Tree.SelectedItem;
            if (treeViewData != null)
            {
                this.Tree.GetItem(treeViewData).IsSelected = false;
            }

            _rootObject = rootObject;
            NotifyPropertyChanged.FirePropertyChanged("RootObject", this, this.PropertyChanged);

            _finder.SetObjects(rootObject);
            _rootObject.SetTreeItemsSource(this.Tree);

            if (_rootObject.TotalChildCount <= 50)
            {
                this.Tree.ExpandAll();
            }

            if (oldSelectionIndex.HasValue && oldSelectionIndex.Value < _rootObject.AllChildren.Count)
            {
                this.Tree.SelectItem(_rootObject.AllChildren[oldSelectionIndex.Value].ViewObject);
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

            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;

            this.UpdateWarnings();
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
            string initialText = App.Current.InitialText;
            App.Current.InitialText = string.Empty;

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
                this.ErrorBanner.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.ErrorBanner.Text = message;
                this.ErrorBanner.Visibility = Visibility.Visible;
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

        private void CommandBinding_Find(object sender, ExecutedRoutedEventArgs e)
        {
            _finder.ShowWindow();
        }

        private void CheckForUpdates(object sender, RoutedEventArgs e)
        {
            App.Current.CheckForUpdates();
        }

        private void OnSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "ConfigPath":
                    this.SetWarningMessage(
                        "Your default config file has changed.  Would you like to reload?",
                        () =>
                        {
                            this.Toolbar.ReloadCommand.Execute(null);
                            this.UpdateWarnings();
                        },
                        () =>
                        {
                            this.UpdateWarnings();
                        });
                    break;
                case "MainWindowWarnOnDefaultConfig":
                    this.UpdateWarnings();
                    break;
            }
        }

        private void UpdateWarnings()
        {
            this.ClearWarningMessage();

            if (Config.This.IsDefault && Properties.Settings.Default.MainWindowWarnOnDefaultConfig)
            {
                this.SetWarningMessage(
                    "Currently using the default configuration.  Do you want to pick a better configuration?",
                    () =>
                    {
                        this.Toolbar.PickConfigCommand.Execute(null);
                        this.UpdateWarnings();
                    },
                    () =>
                    {
                        Properties.Settings.Default.MainWindowWarnOnDefaultConfig = false;
                        Properties.Settings.Default.Save();
                    });
            }
        }

        private void ClearWarningMessage()
        {
            this.WarningBanner.Visibility = Visibility.Collapsed;
            this._warningBannerAction = null;
            this._warningBannerDismiss = null;
        }

        private void SetWarningMessage(string message, WarningBannerActionHandler onAction, WarningBannerActionHandler onDismiss)
        {
            Debug.Assert(!string.IsNullOrEmpty(message));

            this.WarningBanner.Visibility = Visibility.Visible;
            this.WarningBannerActionLink.Inlines.Clear();
            this.WarningBannerActionLink.Inlines.Add(new System.Windows.Documents.Run(message));
            this._warningBannerAction = onAction;
            this._warningBannerDismiss = onDismiss;
        }

        private void OnWarningBannerDismiss(object sender, RoutedEventArgs e)
        {
            Debug.Assert(this._warningBannerDismiss != null);
            this._warningBannerDismiss?.Invoke();
        }

        private void OnWarningBannerAction(object sender, RoutedEventArgs e)
        {
            Debug.Assert(this._warningBannerAction != null);
            this._warningBannerAction?.Invoke();
        }
    }
}
