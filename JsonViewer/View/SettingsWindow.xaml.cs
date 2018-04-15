namespace JsonViewer.View
{
    using System.ComponentModel;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using JsonViewer.Model;
    using Utilities;

    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : System.Windows.Window, INotifyPropertyChanged
    {
        private ConfigValues _values;
        private bool _isEditCommitting = false;
        private EditableRuleSet _ruleSet;

        public SettingsWindow(System.Windows.Window window)
        {
            _values = Config.Values.Clone();
            _values.PropertyChanged += OnValuesPropertyChanged;
            _ruleSet = new EditableRuleSet(_values);

            InitializeComponent();
            this.Owner = window;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public EditableRuleSet RuleSet { get => _ruleSet; set => _ruleSet = value; }

        public double DefaultFontSize
        {
            get => _values.DefaultFontSize;
            set => _values.DefaultFontSize = value;
        }

        public Color DefaultForegroundColor
        {
            get => _values.GetColor(ConfigValue.DefaultForeground);
            set => _values.DefaultForeground = value.GetName();
        }

        public Color DefaultBackgroundColor
        {
            get => _values.GetColor(ConfigValue.DefaultBackground);
            set => _values.DefaultBackground = value.GetName();
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
            set => _values.SelectedForeground = value.GetName();
        }

        public Color SelectedBackgroundColor
        {
            get => _values.GetColor(ConfigValue.SelectedBackground);
            set => _values.SelectedBackground = value.GetName();
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
            set => _values.SearchResultForeground = value.GetName();
        }

        public Color SearchResultBackgroundColor
        {
            get => _values.GetColor(ConfigValue.SearchResultBackground);
            set => _values.SearchResultBackground = value.GetName();
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
            set => _values.SimilarNodeForeground = value.GetName();
        }

        public Color SimilarNodeBackgroundColor
        {
            get => _values.GetColor(ConfigValue.SimilarNodeBackground);
            set => _values.SimilarNodeBackground = value.GetName();
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
            set => _values.SelectedParentForeground = value.GetName();
        }

        public Color SelectedParentBackgroundColor
        {
            get => _values.GetColor(ConfigValue.SelectedParentBackground);
            set => _values.SelectedParentBackground = value.GetName();
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
            _ruleSet.Save();
            await Config.SetValues(_values);
            this.Close();
        }

        private void OnCancelButtonClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void RulesList_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
        {
            string columnHeader = e.Column.Header as string;
            if (!_isEditCommitting && (columnHeader == "Color" || columnHeader == "Font size"))
            {
                _isEditCommitting = true;
                ((DataGrid)sender).CommitEdit(DataGridEditingUnit.Row, true);
                _isEditCommitting = false;
            }
        }

        private void RulesList_AddingNewItem(object sender, AddingNewItemEventArgs e)
        {
        }

        private void RulesList_InitializingNewItem(object sender, InitializingNewItemEventArgs e)
        {
        }
    }
}
