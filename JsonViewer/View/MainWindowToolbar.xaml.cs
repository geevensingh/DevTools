namespace JsonViewer
{
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;
    using JsonViewer.Commands;
    using JsonViewer.Commands.PerWindow;
    using JsonViewer.View;
    using Utilities;

    /// <summary>
    /// Interaction logic for MainWindowToolbar.xaml
    /// </summary>
    public partial class MainWindowToolbar : UserControl, INotifyPropertyChanged
    {
        private MainWindow _mainWindow = null;

        private FindMatchNavigator _findMatchNavigator = null;
        private NewWindowCommand _newWindowCommand = null;
        private PickConfigCommand _pickConfigCommand = null;
        private ReloadCommand _reloadCommand = null;
        private HideFindCommand _hideFindCommand = null;
        private OpenJsonFileCommand _openJsonFileCommand = null;
        private HighlightParentsToggleCommand _highlightParentsToggleCommand = null;
        private HighlightSimilarKeysToggleCommand _highlightSimilarKeysToggleCommand = null;
        private HighlightSimilarValuesToggleCommand _highlightSimilarValuesToggleCommand = null;
        private ExpandAllCommand _expandAllCommand = null;
        private CollapseAllCommand _collapseAllCommand = null;
        private ShowToolbarTextToggleCommand _showToolbarTextToggleCommand = null;
        private ShowToolbarIconToggleCommand _showToolbarIconToggleCommand = null;
        private FindNextCommand _findNextCommand = null;
        private FindPreviousCommand _findPreviousCommand = null;
        private PasteCommand _pasteCommand = null;
        private AutoPasteToggleCommand _autoPasteToggleCommand = null;

        public MainWindowToolbar()
        {
            InitializeComponent();

            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public BaseCommand[] AllCommands { get => new BaseCommand[] { _newWindowCommand, _pickConfigCommand, _reloadCommand, _hideFindCommand, _openJsonFileCommand, _highlightParentsToggleCommand, _highlightSimilarKeysToggleCommand, _highlightSimilarValuesToggleCommand, _expandAllCommand, _collapseAllCommand, _showToolbarTextToggleCommand, _showToolbarIconToggleCommand, _findNextCommand, _findPreviousCommand, _pasteCommand, _autoPasteToggleCommand }; }

        public NewWindowCommand NewWindowCommand { get => _newWindowCommand; }

        public PickConfigCommand PickConfigCommand { get => _pickConfigCommand; }

        public ReloadCommand ReloadCommand { get => _reloadCommand; }

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

        public AutoPasteToggleCommand AutoPasteToggleCommand { get => _autoPasteToggleCommand; }

        public bool ShowToolbarText { get => Properties.Settings.Default.MainWindowToolbarTextVisible; }

        public Visibility ToolbarTextVisibility { get => this.ShowToolbarText ? Visibility.Visible : Visibility.Collapsed; }

        public bool ShowToolbarIcon { get => Properties.Settings.Default.MainWindowToolbarIconVisible; }

        public Visibility ToolbarIconVisibility { get => this.ShowToolbarIcon ? Visibility.Visible : Visibility.Collapsed; }

        public FindMatchNavigator FindMatchNavigator { get => _findMatchNavigator; }

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
            _mainWindow = (MainWindow)this.DataContext;
            Debug.Assert(_mainWindow != null);

            NotifyPropertyChanged.SetValue(ref _newWindowCommand, new NewWindowCommand(_mainWindow), "NewWindowCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _pickConfigCommand, new PickConfigCommand(_mainWindow), "PickConfigCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _reloadCommand, new ReloadCommand(_mainWindow), "ReloadCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _hideFindCommand, new HideFindCommand(_mainWindow), "HideFindCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _openJsonFileCommand, new OpenJsonFileCommand(_mainWindow), "OpenJsonFileCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _highlightParentsToggleCommand, new HighlightParentsToggleCommand(), "HighlightParentsToggleCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _highlightSimilarKeysToggleCommand, new HighlightSimilarKeysToggleCommand(), "HighlightSimilarKeysToggleCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _highlightSimilarValuesToggleCommand, new HighlightSimilarValuesToggleCommand(), "HighlightSimilarValuesToggleCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _expandAllCommand, new ExpandAllCommand(_mainWindow), "ExpandAllCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _collapseAllCommand, new CollapseAllCommand(_mainWindow), "CollapseAllCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _showToolbarTextToggleCommand, new ShowToolbarTextToggleCommand(), "ShowToolbarTextToggleCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _showToolbarIconToggleCommand, new ShowToolbarIconToggleCommand(), "ShowToolbarIconToggleCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _findNextCommand, new FindNextCommand(_mainWindow), "FindNextCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _findPreviousCommand, new FindPreviousCommand(_mainWindow), "FindPreviousCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _pasteCommand, new PasteCommand(_mainWindow), "PasteCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.SetValue(ref _autoPasteToggleCommand, new AutoPasteToggleCommand(_pasteCommand), "AutoPasteToggleCommand", this, this.PropertyChanged);
            NotifyPropertyChanged.FirePropertyChanged("CommandsCreated", this, this.PropertyChanged);

            NotifyPropertyChanged.SetValue(ref _findMatchNavigator, new FindMatchNavigator(_mainWindow), "FindMatchNavigator", this, this.PropertyChanged);

            _mainWindow.Finder.PropertyChanged += OnFinderPropertyChanged;
            FindTextBox.Text = _mainWindow.Finder.Text;
        }

        private void OnFinderPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Debug.Assert(sender == _mainWindow.Finder);
            switch (e.PropertyName)
            {
                case "Text":
                    this.FindTextBox.Text = _mainWindow.Finder.Text;
                    break;
            }
        }

        private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _mainWindow.Finder.Text = FindTextBox.Text;
        }
    }
}
