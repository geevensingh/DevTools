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

        private int? _currentHitIndex = null;

        private int? _currentIndex = null;

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

        public int? CurrentIndex { get => _currentIndex; }

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

            _mainWindow.Tree.SelectedItemChanged += OnTreeSelectedItemChanged;

            _mainWindow.Finder.PropertyChanged += OnFinderPropertyChanged;
            FindTextBox.Text = _mainWindow.Finder.Text;

            this.HighlightParentsButton.IsChecked = Properties.Settings.Default.HighlightSelectedParents;
        }

        private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            Debug.Assert(sender == _mainWindow.Tree);
            TreeViewData newSelectedData = (TreeViewData)e.NewValue;

            _currentHitIndex = null;
            int? newIndex = null;
            if (newSelectedData != null)
            {
                newIndex = newSelectedData.JsonObject.OverallIndex;
                if (newSelectedData.JsonObject.IsFindMatch)
                {
                    _currentHitIndex = _mainWindow.Finder.Hits.IndexOf(newSelectedData.JsonObject);
                }
            }

            NotifyPropertyChanged.SetValue(ref _currentIndex, newIndex, "CurrentIndex", this, this.PropertyChanged);
            UpdateFindMatches();
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
                    _currentHitIndex = null;
                    UpdateFindMatches();
                    break;
            }
        }

        private void UpdateFindMatches()
        {
            Finder finder = _mainWindow.Finder;
            if (finder.HitCount == 0)
            {
                Debug.Assert(!_currentHitIndex.HasValue);
                NotifyPropertyChanged.SetValue(ref _findMatchText, string.Empty, new string[] { "ShowFindControls", "FindMatchText" }, this, this.PropertyChanged);
                return;
            }

            string findMatchText = $"?? / {finder.HitCount}";
            if (_currentHitIndex.HasValue)
            {
                findMatchText = $"{_currentHitIndex.Value + 1} / {finder.HitCount}";
            }

            NotifyPropertyChanged.SetValue(ref _findMatchText, findMatchText, new string[] { "ShowFindControls", "FindMatchText" }, this, this.PropertyChanged);
        }

        private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _mainWindow.Finder.Text = FindTextBox.Text;
        }

        private void FindNextButton_Click(object sender, RoutedEventArgs e)
        {
            GetHitIndexRange(out int previous, out int next);

            _currentHitIndex = (previous + 1) % _mainWindow.Finder.Hits.Count;
            JsonObject hit = _mainWindow.Finder.Hits[_currentHitIndex.Value];
            _mainWindow.Tree.ExpandToItem(hit.ViewObject);
            UpdateFindMatches();
        }

        private void FindPreviousButton_Click(object sender, RoutedEventArgs e)
        {
            GetHitIndexRange(out int previous, out int next);

            _currentHitIndex = (next + _mainWindow.Finder.Hits.Count - 1) % _mainWindow.Finder.Hits.Count;
            JsonObject hit = _mainWindow.Finder.Hits[_currentHitIndex.Value];
            _mainWindow.Tree.ExpandToItem(hit.ViewObject);
            UpdateFindMatches();
        }

        private void GetHitIndexRange(out int previous, out int next)
        {
            if (_currentHitIndex.HasValue)
            {
                previous = _currentHitIndex.Value;
                next = _currentHitIndex.Value;
                return;
            }

            Debug.Assert(_mainWindow.Finder.HitCount > 0);
            if (_mainWindow.Finder.HitCount == 1)
            {
                previous = 1;
                next = 1;
                return;
            }

            previous = -1;
            if (_currentIndex.HasValue)
            {
                Debug.Assert(_mainWindow.Finder.HitCount == _mainWindow.Finder.Hits.Count);
                for (int ii = 0; ii < _mainWindow.Finder.Hits.Count; ii++)
                {
                    JsonObject foo = _mainWindow.Finder.Hits[ii];
                    if (foo.OverallIndex <= _currentIndex.Value)
                    {
                        previous = ii;
                    }
                }
            }

            previous = (previous + _mainWindow.Finder.HitCount) % _mainWindow.Finder.HitCount;
            next = (previous + 1 + _mainWindow.Finder.HitCount) % _mainWindow.Finder.HitCount;

            Debug.Assert(previous >= 0);
            Debug.Assert(previous < _mainWindow.Finder.HitCount);

            Debug.Assert(next >= 0);
            Debug.Assert(next < _mainWindow.Finder.HitCount);
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
