namespace JsonViewer
{
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;

    /// <summary>
    /// Interaction logic for MainWindowToolbar.xaml
    /// </summary>
    public partial class MainWindowToolbar : UserControl, INotifyPropertyChanged
    {
        private MainWindow _mainWindow = null;

        private string _findMatchText = string.Empty;

        private int _currentHitIndex = 0;

        public MainWindowToolbar()
        {
            InitializeComponent();

            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public Visibility ToolbarTextVisibility { get => Properties.Settings.Default.MainWindowToolbarTextVisible ? Visibility.Visible : Visibility.Collapsed; }

        public Visibility ToolbarIconVisibility { get => Properties.Settings.Default.MainWindowToolbarIconVisible ? Visibility.Visible : Visibility.Collapsed; }

        public Visibility ShowFindControls { get => string.IsNullOrEmpty(_findMatchText) ? Visibility.Collapsed : Visibility.Visible; }

        public string FindMatchText { get => _findMatchText; }

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

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _mainWindow = (MainWindow)this.DataContext;
            Debug.Assert(_mainWindow != null);

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
                case "HitCount":
                case "Hits":
                    _currentHitIndex = 0;
                    UpdateFindMatches();
                    break;
            }
        }

        private void UpdateFindMatches()
        {
            Finder finder = _mainWindow.Finder;
            if (finder.HitCount == 0)
            {
                NotifyPropertyChanged.SetValue(ref _findMatchText, string.Empty, new string[] { "ShowFindControls", "FindMatchText" }, this, this.PropertyChanged);
                return;
            }

            string findMatchText = $"{_currentHitIndex + 1} / {finder.HitCount}";
            NotifyPropertyChanged.SetValue(ref _findMatchText, findMatchText, new string[] { "ShowFindControls", "FindMatchText" }, this, this.PropertyChanged);
        }

        private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _mainWindow.Finder.Text = FindTextBox.Text;
        }

        private void FindNextButton_Click(object sender, RoutedEventArgs e)
        {
            _currentHitIndex = (_currentHitIndex + 1) % _mainWindow.Finder.Hits.Count;
            JsonObject hit = _mainWindow.Finder.Hits[_currentHitIndex];
            _mainWindow.Tree.ExpandToItem(hit.ViewObject);
            UpdateFindMatches();
        }

        private void FindPreviousButton_Click(object sender, RoutedEventArgs e)
        {
            _currentHitIndex = (_currentHitIndex + _mainWindow.Finder.Hits.Count - 1) % _mainWindow.Finder.Hits.Count;
            JsonObject hit = _mainWindow.Finder.Hits[_currentHitIndex];
            _mainWindow.Tree.ExpandToItem(hit.ViewObject);
            UpdateFindMatches();
        }

#pragma warning disable SA1201 // Elements must appear in the correct order
#pragma warning disable SA1400 // Access modifier must be declared
        static int foo = 0;

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            switch (foo++ % 3)
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
#pragma warning restore SA1400 // Access modifier must be declared
#pragma warning restore SA1201 // Elements must appear in the correct order
    }
}
