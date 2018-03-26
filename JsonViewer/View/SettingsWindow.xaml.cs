namespace JsonViewer.View
{
    using System.ComponentModel;
    using System.Windows;
    using System.Windows.Media;
    using JsonViewer.Model;
    using Utilities;

    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window, INotifyPropertyChanged
    {
        private ConfigValues _values;

        public SettingsWindow(MainWindow mainWindow)
        {
            _values = Config.Values.Clone();
            _values.PropertyChanged += OnValuesPropertyChanged;
            InitializeComponent();
            this.Owner = mainWindow;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public double DefaultFontSize
        {
            get => _values.DefaultFontSize;
            set => _values.DefaultFontSize = value;
        }

        public Color DefaultForegroundColor
        {
            get => _values.GetColor(ConfigValue.DefaultForeground);
            set => _values.DefaultForeground = value;
        }

        public Color DefaultBackgroundColor
        {
            get => _values.GetColor(ConfigValue.DefaultBackground);
            set => _values.DefaultBackground = value;
        }

        public Brush DefaultForegroundBrush
        {
            get => _values.GetBrush(ConfigValue.DefaultForeground);
        }

        public Brush DefaultBackgroundBrush
        {
            get => _values.GetBrush(ConfigValue.DefaultBackground);
        }

        public Color SelectedForegroundColor
        {
            get => _values.GetColor(ConfigValue.SelectedForeground);
            set => _values.SelectedForeground = value;
        }

        public Color SelectedBackgroundColor
        {
            get => _values.GetColor(ConfigValue.SelectedBackground);
            set => _values.SelectedBackground = value;
        }

        public Brush SelectedForegroundBrush
        {
            get => _values.GetBrush(ConfigValue.SelectedForeground);
        }

        public Brush SelectedBackgroundBrush
        {
            get => _values.GetBrush(ConfigValue.SelectedBackground);
        }

        public Color SearchResultForegroundColor
        {
            get => _values.GetColor(ConfigValue.SearchResultForeground);
            set => _values.SearchResultForeground = value;
        }

        public Color SearchResultBackgroundColor
        {
            get => _values.GetColor(ConfigValue.SearchResultBackground);
            set => _values.SearchResultBackground = value;
        }

        public Brush SearchResultForegroundBrush
        {
            get => _values.GetBrush(ConfigValue.SearchResultForeground);
        }

        public Brush SearchResultBackgroundBrush
        {
            get => _values.GetBrush(ConfigValue.SearchResultBackground);
        }

        public Color SimilarNodeForegroundColor
        {
            get => _values.GetColor(ConfigValue.SimilarNodeForeground);
            set => _values.SimilarNodeForeground = value;
        }

        public Color SimilarNodeBackgroundColor
        {
            get => _values.GetColor(ConfigValue.SimilarNodeBackground);
            set => _values.SimilarNodeBackground = value;
        }

        public Brush SimilarNodeForegroundBrush
        {
            get => _values.GetBrush(ConfigValue.SimilarNodeForeground);
        }

        public Brush SimilarNodeBackgroundBrush
        {
            get => _values.GetBrush(ConfigValue.SimilarNodeBackground);
        }

        public Color SelectedParentForegroundColor
        {
            get => _values.GetColor(ConfigValue.SelectedParentForeground);
            set => _values.SelectedParentForeground = value;
        }

        public Color SelectedParentBackgroundColor
        {
            get => _values.GetColor(ConfigValue.SelectedParentBackground);
            set => _values.SelectedParentBackground = value;
        }

        public Brush SelectedParentForegroundBrush
        {
            get => _values.GetBrush(ConfigValue.SelectedParentForeground);
        }

        public Brush SelectedParentBackgroundBrush
        {
            get => _values.GetBrush(ConfigValue.SelectedParentBackground);
        }

        private void OnValuesPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "DefaultForeground":
                case "DefaultBackground":
                case "SelectedForeground":
                case "SelectedBackground":
                case "SearchResultForeground":
                case "SearchResultBackground":
                case "SimilarNodeForeground":
                case "SimilarNodeBackground":
                case "SelectedParentForeground":
                case "SelectedParentBackground":
                    NotifyPropertyChanged.FirePropertyChanged(e.PropertyName + "Color", this, this.PropertyChanged);
                    NotifyPropertyChanged.FirePropertyChanged(e.PropertyName + "Brush", this, this.PropertyChanged);
                    break;
                case "DefaultFontSize":
                    NotifyPropertyChanged.FirePropertyChanged(e.PropertyName, this, this.PropertyChanged);
                    break;
                default:
                    break;
            }
        }

        private async void OnSaveButtonClick(object sender, RoutedEventArgs e)
        {
            await Config.SetValues(_values);
            this.Close();
        }

        private void OnCancelButtonClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
