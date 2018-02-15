namespace JsonViewer
{
    using System.ComponentModel;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;

    /// <summary>
    /// Interaction logic for FindWindow.xaml
    /// </summary>
    public partial class FindWindow : Window, INotifyPropertyChanged
    {
        private Finder _finder;
        private bool _allowEvents = false;

        private string _text = string.Empty;
        private int _hitCount = 0;

        internal FindWindow(Window owner, Finder finder)
        {
            InitializeComponent();
            this.Owner = owner;
            _finder = finder;
            _text = _finder.Text;
            _hitCount = _finder.HitCount;
            this.DataContext = this;
        }

        public delegate void FindTextChangedEventHandler(string oldText, string newText);

        public delegate void FindOptionsChangedEventHandler();

        public event FindTextChangedEventHandler FindTextChanged;

        public event FindOptionsChangedEventHandler FindOptionsChanged;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Text { get => _text; }

        public bool ShouldSearchKeys { get => this.searchKeysCheckbox.IsChecked.Value; }

        public bool ShouldSearchValues { get => this.searchValuesCheckbox.IsChecked.Value; }

        public bool ShouldSearchParentValues { get => this.searchParentValuesCheckbox.IsChecked.Value; }

        public bool ShouldIgnoreCase { get => this.ignoreCaseCheckbox.IsChecked.Value; }

        public int HitCount { get => _hitCount; }

        public Visibility HitCountVisible { get => (_hitCount > 0) ? Visibility.Visible : Visibility.Collapsed; }

        internal void SetHitCount(int hitCount)
        {
            _hitCount = hitCount;
            if (this.PropertyChanged != null)
            {
                this.PropertyChanged(this, new PropertyChangedEventArgs("HitCount"));
                this.PropertyChanged(this, new PropertyChangedEventArgs("HitCountVisible"));
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            this.ignoreCaseCheckbox.IsChecked = _finder.ShouldIgnoreCase;
            this.searchKeysCheckbox.IsChecked = _finder.ShouldSearchKeys;
            this.searchValuesCheckbox.IsChecked = _finder.ShouldSearchValues;
            this.searchParentValuesCheckbox.IsChecked = _finder.ShouldSearchParentValues;
            this.textBox.Focus();
            _allowEvents = true;
        }

        private void OnTextBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            string oldText = _text;
            _text = this.textBox.Text;
            if (_allowEvents && this.FindTextChanged != null)
            {
                this.FindTextChanged(oldText, _text);
            }
        }

        private void OnChecked(object sender, RoutedEventArgs e)
        {
            if (_allowEvents && this.FindOptionsChanged != null)
            {
                this.FindOptionsChanged();
            }
        }

        private void Tree_CommandBinding_HideFind(object sender, ExecutedRoutedEventArgs e)
        {
            CommandFactory.HideFind_Execute(_finder);
        }
    }
}
