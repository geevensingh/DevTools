namespace JsonViewer.View
{
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using JsonViewer.Commands.PerWindow;
    using JsonViewer.Model;
    using JsonViewer.View;
    using Utilities;

    /// <summary>
    /// Interaction logic for FindWindow.xaml
    /// </summary>
    public partial class FindWindow : System.Windows.Window, INotifyPropertyChanged
    {
        private TabContent _tab;
        private Finder _finder;
        private FindMatchNavigator _navigator;

        internal FindWindow(TabContent owner, Finder finder)
        {
            _tab = owner;
            _finder = finder;
            _finder.PropertyChanged += OnViewModelPropertyChanged;

            _navigator = new FindMatchNavigator(owner);

            InitializeComponent();
            this.Owner = owner;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public Finder ViewModel { get => _finder; }

        public Visibility HitCountVisible { get => (_finder.HitCount > 0 || !string.IsNullOrEmpty(_finder.Text)) ? Visibility.Visible : Visibility.Collapsed; }

        public FindMatchNavigator FindMatchNavigator { get => _navigator; }

        public HideFindCommand HideFindCommand { get => _tab.Toolbar.HideFindCommand; }

        public FindNextCommand FindNextCommand { get => _tab.Toolbar.FindNextCommand; }

        public FindPreviousCommand FindPreviousCommand { get => _tab.Toolbar.FindPreviousCommand; }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            FileLogger.Assert(sender == _finder);
            switch (e.PropertyName)
            {
                case "Text":
                    NotifyPropertyChanged.FirePropertyChanged("HitCountVisible", this, this.PropertyChanged);
                    break;
                case "HitCount":
                    NotifyPropertyChanged.FirePropertyChanged(new string[] { "HitCount", "HitCountVisible" }, this, this.PropertyChanged);
                    break;
                case "ShouldSearchKeys":
                case "ShouldSearchValues":
                case "ShouldSearchParentValues":
                case "ShouldSearchValueTypes":
                case "ShouldIgnoreCase":
                case "HasWindow":
                case "Hits":
                    break;
                default:
                    Debug.Fail("Unknown property name");
                    break;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            this.textBox.Text = _finder.Text;
            this.textBox.Focus();
            this.textBox.Select(0, _finder.Text.Length);
        }

        private void Tree_CommandBinding_HideFind(object sender, ExecutedRoutedEventArgs e)
        {
            _finder.HideWindow();
        }

        private void OnTextBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            FileLogger.Assert(sender == this.textBox);
            _finder.Text = this.textBox.Text;
        }
    }
}
