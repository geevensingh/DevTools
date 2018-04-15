namespace JsonViewer.View
{
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;
    using JsonViewer.Commands;
    using JsonViewer.Commands.PerWindow;
    using JsonViewer.Model;
    using JsonViewer.View;
    using Utilities;

    /// <summary>
    /// Interaction logic for MainWindowToolbar.xaml
    /// </summary>
    public partial class MainWindowToolbar : UserControl, INotifyPropertyChanged
    {
        private TabContent _tab = null;

        private FindMatchNavigator _findMatchNavigator = null;

        private AutoPasteToggleCommand _autoPasteToggleCommand = null;
        private CollapseAllCommand _collapseAllCommand = null;
        private ExpandAllCommand _expandAllCommand = null;
        private FindNextCommand _findNextCommand = null;
        private FindPreviousCommand _findPreviousCommand = null;
        private HideFindCommand _hideFindCommand = null;
        private HighlightParentsToggleCommand _highlightParentsToggleCommand = null;
        private HighlightSimilarKeysToggleCommand _highlightSimilarKeysToggleCommand = null;
        private HighlightSimilarValuesToggleCommand _highlightSimilarValuesToggleCommand = null;
        private NewWindowCommand _newWindowCommand = null;
        private OpenJsonFileCommand _openJsonFileCommand = null;
        private PasteCommand _pasteCommand = null;
        private PickConfigCommand _pickConfigCommand = null;
        private PrettyCopyAllCommand _prettyCopyAllCommand = null;
        private PrettyTextCommand _prettyTextCommand = null;
        private ReloadCommand _reloadCommand = null;
        private SettingsCommand _settingsCommand = null;
        private ShowToolbarIconToggleCommand _showToolbarIconToggleCommand = null;
        private ShowToolbarTextToggleCommand _showToolbarTextToggleCommand = null;
        private SwitchModeCommand _showTextModeCommand = null;
        private SwitchModeCommand _showTreeModeCommand = null;

        public MainWindowToolbar()
        {
            InitializeComponent();

            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public BaseCommand[] AllCommands { get => new BaseCommand[] { _autoPasteToggleCommand, _collapseAllCommand, _expandAllCommand, _findNextCommand, _findPreviousCommand, _hideFindCommand, _highlightParentsToggleCommand, _highlightSimilarKeysToggleCommand, _highlightSimilarValuesToggleCommand, _newWindowCommand, _openJsonFileCommand, _pasteCommand, _pickConfigCommand, _prettyCopyAllCommand, _prettyTextCommand, _reloadCommand, _settingsCommand, _showTextModeCommand, _showToolbarIconToggleCommand, _showToolbarTextToggleCommand, _showTreeModeCommand }; }

        public NewWindowCommand NewWindowCommand { get => _newWindowCommand; }

        public PickConfigCommand PickConfigCommand { get => _pickConfigCommand; }

        public ReloadCommand ReloadCommand { get => _reloadCommand; }

        public SettingsCommand SettingsCommand { get => _settingsCommand; }

        public HideFindCommand HideFindCommand { get => _hideFindCommand; }

        public OpenJsonFileCommand OpenJsonFileCommand { get => _openJsonFileCommand; }

        public HighlightParentsToggleCommand HighlightParentsToggleCommand { get => _highlightParentsToggleCommand; }

        public HighlightSimilarKeysToggleCommand HighlightSimilarKeysToggleCommand { get => _highlightSimilarKeysToggleCommand; }

        public HighlightSimilarValuesToggleCommand HighlightSimilarValuesToggleCommand { get => _highlightSimilarValuesToggleCommand; }

        public ExpandAllCommand ExpandAllCommand { get => _expandAllCommand; }

        public CollapseAllCommand CollapseAllCommand { get => _collapseAllCommand; }

        public ShowToolbarTextToggleCommand ShowToolbarTextToggleCommand { get => _showToolbarTextToggleCommand; }

        public ShowToolbarIconToggleCommand ShowToolbarIconToggleCommand { get => _showToolbarIconToggleCommand; }

        public FindNextCommand FindNextCommand { get => _findNextCommand; }

        public FindPreviousCommand FindPreviousCommand { get => _findPreviousCommand; }

        public PasteCommand PasteCommand { get => _pasteCommand; }

        public PrettyCopyAllCommand PrettyCopyAllCommand { get => _prettyCopyAllCommand; }

        public PrettyTextCommand PrettyTextCommand { get => _prettyTextCommand; }

        public AutoPasteToggleCommand AutoPasteToggleCommand { get => _autoPasteToggleCommand; }

        public SwitchModeCommand ShowTextModeCommand { get => _showTextModeCommand; }

        public SwitchModeCommand ShowTreeModeCommand { get => _showTreeModeCommand; }

        public bool ShowToolbarText { get => Properties.Settings.Default.MainWindowToolbarTextVisible; }

        public Visibility ToolbarTextVisibility { get => this.ShowToolbarText ? Visibility.Visible : Visibility.Collapsed; }

        public bool ShowToolbarIcon { get => Properties.Settings.Default.MainWindowToolbarIconVisible; }

        public Visibility ToolbarIconVisibility { get => this.ShowToolbarIcon ? Visibility.Visible : Visibility.Collapsed; }

        public FindMatchNavigator FindMatchNavigator { get => _findMatchNavigator; }

        public Visibility TextModeVisibility { get => (_tab != null && _tab.Mode == TabContent.DisplayMode.RawText) ? Visibility.Visible : Visibility.Collapsed; }

        public Visibility TreeModeVisibility { get => (_tab != null && _tab.Mode == TabContent.DisplayMode.TreeView) ? Visibility.Visible : Visibility.Collapsed; }

        private void OnSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "MainWindowToolbarTextVisible":
                    NotifyPropertyChanged.FirePropertyChanged(new string[] { "ShowToolbarText", "ToolbarTextVisibility" }, this, this.PropertyChanged);
                    break;
                case "MainWindowToolbarIconVisible":
                    NotifyPropertyChanged.FirePropertyChanged(new string[] { "ShowToolbarIcon", "ToolbarIconVisibility" }, this, this.PropertyChanged);
                    break;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _tab = (TabContent)this.DataContext;
            FileLogger.Assert(_tab != null);
            NotifyPropertyChanged.FirePropertyChanged(new string[] { "TextModeVisibility", "TreeModeVisibility" }, this, this.PropertyChanged);

            NotifyPropertyChanged.SetValue(ref _newWindowCommand, new NewWindowCommand(_tab), "NewWindowCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _pickConfigCommand, new PickConfigCommand(_tab), "PickConfigCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _reloadCommand, new ReloadCommand(), "ReloadCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _settingsCommand, new SettingsCommand(_tab), "SettingsCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _hideFindCommand, new HideFindCommand(_tab), "HideFindCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _openJsonFileCommand, new OpenJsonFileCommand(_tab), "OpenJsonFileCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _highlightParentsToggleCommand, new HighlightParentsToggleCommand(), "HighlightParentsToggleCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _highlightSimilarKeysToggleCommand, new HighlightSimilarKeysToggleCommand(), "HighlightSimilarKeysToggleCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _highlightSimilarValuesToggleCommand, new HighlightSimilarValuesToggleCommand(), "HighlightSimilarValuesToggleCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _expandAllCommand, new ExpandAllCommand(_tab), "ExpandAllCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _collapseAllCommand, new CollapseAllCommand(_tab), "CollapseAllCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _showToolbarTextToggleCommand, new ShowToolbarTextToggleCommand(), "ShowToolbarTextToggleCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _showToolbarIconToggleCommand, new ShowToolbarIconToggleCommand(), "ShowToolbarIconToggleCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _findNextCommand, new FindNextCommand(_tab), "FindNextCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _findPreviousCommand, new FindPreviousCommand(_tab), "FindPreviousCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _pasteCommand, new PasteCommand(_tab), "PasteCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _prettyCopyAllCommand, new PrettyCopyAllCommand(_tab), "PrettyCopyAllCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _prettyTextCommand, new PrettyTextCommand(_tab), "PrettyTextCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _autoPasteToggleCommand, new AutoPasteToggleCommand(_pasteCommand), "AutoPasteToggleCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _showTextModeCommand, new SwitchModeCommand(_tab, "Show text", TabContent.DisplayMode.RawText), "ShowTextModeCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _showTreeModeCommand, new SwitchModeCommand(_tab, "Show tree", TabContent.DisplayMode.TreeView), "ShowTreeModeCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.FirePropertyChanged("CommandsCreated", this, this.PropertyChanged);

            NotifyPropertyChanged.SetValue(ref _findMatchNavigator, new FindMatchNavigator(_tab), "FindMatchNavigator", this, this.PropertyChanged);

            _tab.PropertyChanged += OnTabPropertyChanged;
            _tab.Finder.PropertyChanged += OnFinderPropertyChanged;
            FindTextBox.Text = _tab.Finder.Text;
        }

        private void OnTabPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "Mode":
                    NotifyPropertyChanged.FirePropertyChanged(new string[] { "TextModeVisibility", "TreeModeVisibility" }, this, this.PropertyChanged);
                    break;
                default:
                    break;
            }
        }

        private void OnFinderPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            FileLogger.Assert(sender == _tab.Finder);
            switch (e.PropertyName)
            {
                case "Text":
                    this.FindTextBox.Text = _tab.Finder.Text;
                    break;
            }
        }

        private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _tab.Finder.Text = FindTextBox.Text;
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(((MenuItem)e.Source).CommandParameter as string, out int depth))
            {
                new ExpandToLevelCommand(_tab, depth).Execute(null);
            }
        }
    }
}
