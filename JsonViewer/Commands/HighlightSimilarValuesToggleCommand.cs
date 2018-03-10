namespace JsonViewer.Commands
{
    public class HighlightSimilarValuesToggleCommand : ToggleCommand
    {
        public HighlightSimilarValuesToggleCommand(MainWindow mainWindow)
            : base("Highlight similar values", Properties.Settings.Default.HighlightSimilarValues)
        {
            this.MainWindow = mainWindow;
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        public override void Execute(object parameter)
        {
            Properties.Settings.Default.HighlightSimilarValues = !Properties.Settings.Default.HighlightSimilarValues;
            Properties.Settings.Default.Save();
        }

        protected override void OnMainWindowPropertyChanged(string propertyName)
        {
            switch (propertyName)
            {
                case "Mode":
                    this.SetCanExecute(this.MainWindow.Mode == MainWindow.DisplayMode.TreeView);
                    break;
            }
        }

        private void OnSettingsPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "HighlightSimilarValues":
                    this.IsChecked = Properties.Settings.Default.HighlightSimilarValues;
                    break;
            }
        }
    }
}
