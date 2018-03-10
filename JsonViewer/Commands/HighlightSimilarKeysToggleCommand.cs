namespace JsonViewer.Commands
{
    public class HighlightSimilarKeysToggleCommand : ToggleCommand
    {
        public HighlightSimilarKeysToggleCommand(MainWindow mainWindow)
            : base("Highlight similar keys", Properties.Settings.Default.HighlightSimilarKeys)
        {
            this.MainWindow = mainWindow;
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        public override void Execute(object parameter)
        {
            Properties.Settings.Default.HighlightSimilarKeys = !Properties.Settings.Default.HighlightSimilarKeys;
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
                case "HighlightSimilarKeys":
                    this.IsChecked = Properties.Settings.Default.HighlightSimilarKeys;
                    break;
            }
        }
    }
}
