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
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private Finder _finder;
        private Point? _initialOffset = null;
        private RootObject _rootObject = null;
        private WarningBannerActionHandler _warningBannerAction;
        private WarningBannerActionHandler _warningBannerDismiss;
        private string _lastText = string.Empty;
        private RuleSet _ruleSet = new RuleSet();
        private bool _isEditCommitting = false;

        public MainWindow()
        {
            _finder = new Finder(this);

            InitializeComponent();
        }

        private delegate void WarningBannerActionHandler();

        public event PropertyChangedEventHandler PropertyChanged;

        public enum DisplayMode
        {
            RawText,
            TreeView,
            Rules
        }

        public DisplayMode Mode { get; private set; }

        public Finder Finder { get => _finder; }

        public RuleSet RuleSet { get => _ruleSet; }

        internal RootObject RootObject { get => _rootObject; }

        internal ClipboardManager ClipboardManager { get; private set; }

        internal SimilarHighlighter SimilarHighlighter { get; private set; }

        public void ShowNewWindow()
        {
            this.SaveWindowPosition();

            MainWindow newWindow = new MainWindow
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
            _ruleSet.Refresh();

            if (string.IsNullOrWhiteSpace(this.Raw_TextBox.Text))
            {
                return Task.FromResult(false);
            }

            return this.ReloadAsync(JsonObjectFactory.TryDeserialize(this.Raw_TextBox.Text)?.Dictionary);
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

            if (_rootObject.TotalChildCount <= 50)
            {
                this.Tree.ExpandAll();
            }

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
                this.RulesList.Visibility = Visibility.Collapsed;
                Control newControl = null;
                switch (newMode)
                {
                    case DisplayMode.RawText:
                        newControl = this.Raw_TextBox;
                        break;
                    case DisplayMode.TreeView:
                        newControl = this.Tree;
                        break;
                    case DisplayMode.Rules:
                        newControl = this.RulesList;
                        break;
                    default:
                        Debug.Assert(false);
                        break;
                }

                Debug.Assert(newControl != null);
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

            _ruleSet.Refresh();

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
                if (JsonObjectFactory.TryDeserialize(jsonString) != null)
                {
                    initialText = jsonString;
                }
            }

            this.Raw_TextBox.Text = initialText;
            this.SetDisplayMode(string.IsNullOrEmpty(initialText) ? DisplayMode.RawText : DisplayMode.TreeView);
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
            Debug.Assert(sender.Equals(this.Raw_TextBox));
            string newText = this.Raw_TextBox.Text;
            Dictionary<string, object> dictionary = JsonObjectFactory.TryDeserialize(newText)?.Dictionary;
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
                        null,
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
                    "Unable to find your configuration file.  Do you want to pick a configuration file?",
                    null,
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
                IEnumerable<ConfigRule> warningRules = Config.This?.Rules?.Where(rule => !string.IsNullOrEmpty(rule.WarningMessage)).Where(x => _rootObject?.AllChildren?.Any(y => y.Rules.Contains(x)) ?? false);
                IEnumerable<string> warnings = warningRules?.Select(x => x.WarningMessage);
                if (warnings != null && warnings.Count() > 0)
                {
                    double? fontSize = warningRules.Max((rule) => rule.FontSize);
                    string warningMessage = string.Join("\r\n", warnings);
                    this.SetWarningMessage(
                        warningMessage,
                        fontSize,
                        null,
                        () =>
                        {
                            foreach (JsonObject jsonObject in _rootObject.AllChildren)
                            {
                                jsonObject.Rules.RemoveAll(y => !string.IsNullOrEmpty(y.WarningMessage));
                            }
                            this.UpdateWarnings();
                        });
                }
            }
        }

        private void ClearWarningMessage()
        {
            this.WarningBanner.Visibility = Visibility.Collapsed;
            this._warningBannerAction = null;
            this._warningBannerDismiss = null;
        }

        private void SetWarningMessage(string message, double? fontSize, WarningBannerActionHandler onAction, WarningBannerActionHandler onDismiss)
        {
            Debug.Assert(!string.IsNullOrEmpty(message));

            if (!fontSize.HasValue)
            {
                fontSize = Config.This.DefaultFontSize;
            }

            this.WarningBanner.Visibility = Visibility.Visible;
            this.WarningBannerActionLink.FontSize = fontSize.Value * 1.5;
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

        private void RulesList_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            string columnHeader = e.Column.Header as string;
            if (!_isEditCommitting && (columnHeader == "Color" || columnHeader == "Font size"))
            {
                _isEditCommitting = true;
                ((DataGrid)sender).CommitEdit(DataGridEditingUnit.Row, true);
                _isEditCommitting = false;
            }
        }

        private void SaveRuleChanges_Click(object sender, RoutedEventArgs e)
        {
            this.RuleSet.Save();
            this.RootObject?.FlushRules();
            this.RootObject?.ApplyExpandRule(this.Tree);
            this.UpdateWarnings();
        }

        private void DiscardRuleChanges_Click(object sender, RoutedEventArgs e)
        {
            this.RuleSet.Discard();
        }
    }
}
