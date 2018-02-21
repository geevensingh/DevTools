namespace JsonViewer
{
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;
    using JsonViewer.Commands;
    using JsonViewer.View;

    /// <summary>
    /// Interaction logic for MainWindowToolbar.xaml
    /// </summary>
    public partial class MainWindowToolbar : UserControl, INotifyPropertyChanged
    {
        private MainWindow _mainWindow = null;

        private FindMatchNavigator _findMatchNavigator = null;
        private NewWindowCommand _newWindowCommand = null;

        public MainWindowToolbar()
        {
            InitializeComponent();

            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public NewWindowCommand NewWindowCommand { get => _newWindowCommand; }

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

            NotifyPropertyChanged.SetValue(ref _findMatchNavigator, new FindMatchNavigator(_mainWindow), "FindMatchNavigator", this, this.PropertyChanged);

            _mainWindow.Finder.PropertyChanged += OnFinderPropertyChanged;
            FindTextBox.Text = _mainWindow.Finder.Text;

            this.HighlightParentsButton.IsChecked = Properties.Settings.Default.HighlightSelectedParents;
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
