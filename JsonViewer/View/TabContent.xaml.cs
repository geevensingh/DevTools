namespace JsonViewer.View
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using JsonViewer.Commands;
    using JsonViewer.Commands.PerWindow;
    using JsonViewer.Model;
    using Utilities;

    /// <summary>
    /// Interaction logic for TabContent.xaml
    /// </summary>
    public partial class TabContent : System.Windows.Window, INotifyPropertyChanged
    {
        private Finder _finder;
        private Point? _initialOffset = null;
        private RootObject _rootObject = null;
        private WarningBannerActionHandler _warningBannerAction;
        private WarningBannerActionHandler _warningBannerDismiss;
        private string _lastText = string.Empty;

        public TabContent()
        {
            _finder = new Finder(this);
            Config.PropertyChanged += OnConfigPropertyChanged;

            InitializeComponent();
        }

        private delegate void WarningBannerActionHandler();

        public event PropertyChangedEventHandler PropertyChanged;

        public enum DisplayMode
        {
            RawText,
            TreeView
        }

        public DisplayMode Mode { get; private set; }

        public Finder Finder { get => _finder; }

        internal RootObject RootObject { get => _rootObject; }

        internal ClipboardManager ClipboardManager { get; private set; }

        internal SimilarHighlighter SimilarHighlighter { get; private set; }

        public void ShowNewWindow()
        {
            this.SaveWindowPosition();

            TabContent newWindow = new TabContent
            {
                _initialOffset = new Point(20, 20)
            };
            newWindow.Show();
        }

        public void LoadConfig(string filePath)
        {
            Properties.Settings.Default.PropertyChanged -= OnSettingsPropertyChanged;
            bool succeeded = Config.SetPath(filePath);
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;

            if (succeeded)
            {
                this.ReloadAsync().Forget();
            }
            else
            {
                MessageBox.Show(this, "Unable to load config: " + filePath, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public Task<bool> ReloadAsync()
        {
            if (string.IsNullOrWhiteSpace(this.Raw_TextBox.Text))
            {
                return Task.FromResult(false);
            }

            return Task.Run<bool>(async () =>
            {
                DeserializeResult deserializeResult = await JsonObjectFactory.TryAgressiveDeserialize(this.Raw_TextBox.Text);
                return await this.ReloadAsync(deserializeResult?.Dictionary);
            });
        }

        public async Task<bool> ReloadAsync(Dictionary<string, object> dictionary)
        {
            RootObject rootObject = await RootObject.Create(dictionary);
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

            if (_rootObject != null)
            {
                _rootObject.PropertyChanged -= OnRootObjectPropertyChanged;
            }

            _rootObject = rootObject;
            NotifyPropertyChanged.FirePropertyChanged("RootObject", this, this.PropertyChanged);
            _rootObject.PropertyChanged += OnRootObjectPropertyChanged;

            _finder.SetObjects(rootObject);
            _rootObject.SetTreeItemsSource(this.Tree);

            if (oldSelectionIndex.HasValue && oldSelectionIndex.Value < _rootObject.AllChildren.Count)
            {
                this.Tree.SelectItem(_rootObject.AllChildren[oldSelectionIndex.Value].ViewObject);
            }

            this.UpdateWarnings();

            return true;
        }

        public void SetDisplayMode(DisplayMode newMode)
        {
            if (newMode != this.Mode)
            {
                this.Raw_TextBox.Visibility = Visibility.Collapsed;
                this.Tree.Visibility = Visibility.Collapsed;
                Control newControl = null;
                switch (newMode)
                {
                    case DisplayMode.RawText:
                        newControl = this.Raw_TextBox;
                        break;
                    case DisplayMode.TreeView:
                        newControl = this.Tree;
                        break;
                    default:
                        FileLogger.Assert(false);
                        break;
                }

                FileLogger.Assert(newControl != null);
                newControl.Visibility = Visibility.Visible;
                newControl.Focus();

                this.Mode = newMode;
                NotifyPropertyChanged.FirePropertyChanged("Mode", this, this.PropertyChanged);
            }
        }

        public void RunWhenever(Action action)
        {
            this.Dispatcher.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            this.ClipboardManager = new ClipboardManager(this);

            this.SimilarHighlighter = new SimilarHighlighter(this);

            this.Toolbar.PropertyChanged += OnToolbarPropertyChanged;

            WindowPlacementSerializer.SetPlacement(this, Properties.Settings.Default.MainWindowPlacement, _initialOffset);
            if (_initialOffset.HasValue)
            {
                this.SaveWindowPosition();
            }

            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;

            this.UpdateWarnings();

            for (int ii = 0; ii < 10; ii++)
            {
                MenuItem menuItem = new MenuItem();
                ExpandToLevelCommand expandToLevelCommand = new ExpandToLevelCommand(this, ii);
                menuItem.Command = expandToLevelCommand;
                menuItem.Header = expandToLevelCommand.Text;
                menuItem.IsCheckable = false;
                this.ExpandToMenuItem.Items.Add(menuItem);
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
            string initialText = App.Current.InitialText;
            App.Current.InitialText = string.Empty;

            if (string.IsNullOrEmpty(initialText) && Clipboard.ContainsText())
            {
                string jsonString = ClipboardManager.TryGetText();
                if (JsonObjectFactory.TryAgressiveDeserialize(jsonString).Result.IsSuccessful())
                {
                    initialText = jsonString;
                }
            }

            this.Raw_TextBox.Text = initialText;
            this.SetDisplayMode(string.IsNullOrEmpty(initialText) ? DisplayMode.RawText : DisplayMode.TreeView);
        }

        private void OnConfigPropertyChanged(string propertyName)
        {
            switch (propertyName)
            {
                case "Values":
                    this.ReloadAsync().Forget();
                    break;
                default:
                    break;
            }
        }

        private void OnToolbarPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "CommandsCreated")
            {
                foreach (BaseCommand command in this.Toolbar.AllCommands)
                {
                    CommandBinding commandBinding = command.CommandBinding;
                    if (commandBinding != null)
                    {
                        this.CommandBindings.Add(commandBinding);
                    }
                }
            }
        }

        private void OnRootObjectPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "AllChildren":
                    this.UpdateWarnings();
                    break;
            }
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
            FileLogger.Assert(sender.Equals(this.Raw_TextBox));
            string newText = this.Raw_TextBox.Text;
            DeserializeResult deserializeResult = await JsonObjectFactory.TryAgressiveDeserialize(newText);
            Dictionary<string, object> dictionary = deserializeResult?.GetEverythingDictionary();
            string newNormalizedText = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(dictionary);
            if (newNormalizedText != _lastText)
            {
                _lastText = newNormalizedText;
                await this.ReloadAsync(dictionary);
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

            if (Config.IsDefault && Properties.Settings.Default.MainWindowWarnOnDefaultConfig)
            {
                this.SetWarningMessage(
                    "Unable to find your configuration file.  Do you want to pick a configuration file?",
                    () =>
                    {
                        this.Toolbar.PickConfigCommand.Execute(null);
                        this.UpdateWarnings();
                    },
                    () =>
                    {
                        Properties.Settings.Default.MainWindowWarnOnDefaultConfig = false;
                        Properties.Settings.Default.Save();
                        this.UpdateWarnings();
                    });
            }
            else
            {
                List<JsonObject> nodes = _rootObject?.AllChildren;
                if (nodes != null)
                {
                    SortedSet<string> set = new SortedSet<string>();
                    foreach (JsonObject obj in nodes)
                    {
                        IEnumerable<string> warningMessages = obj.Rules.WarningMessages;
                        if (warningMessages != null)
                        {
                            foreach (string warningMessage in warningMessages)
                            {
                                set.Add(warningMessage);
                            }
                        }
                    }

                    if (set.Count > 0)
                    {
                        string warningMessage = string.Join("\r\n", set.ToArray());
                        this.SetWarningMessage(
                            warningMessage,
                            null,
                            () =>
                            {
                                foreach (JsonObject jsonObject in _rootObject.AllChildren)
                                {
                                    jsonObject.Rules.DismissWarningMessage();
                                }
                                this.UpdateWarnings();
                            });
                    }
                }
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
            FileLogger.Assert(!string.IsNullOrEmpty(message));

            this.WarningBanner.Visibility = Visibility.Visible;
            this.WarningBannerActionLink.Inlines.Clear();
            this.WarningBannerActionLink.Inlines.Add(new System.Windows.Documents.Run(message));
            this._warningBannerAction = onAction;
            this._warningBannerDismiss = onDismiss;
        }

        private void OnWarningBannerDismiss(object sender, RoutedEventArgs e)
        {
            FileLogger.Assert(this._warningBannerDismiss != null);
            this._warningBannerDismiss?.Invoke();
        }

        private void OnWarningBannerAction(object sender, RoutedEventArgs e)
        {
            FileLogger.Assert(this._warningBannerAction != null);
            this._warningBannerAction?.Invoke();
        }
    }
}
