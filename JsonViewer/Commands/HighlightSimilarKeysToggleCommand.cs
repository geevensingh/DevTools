namespace JsonViewer.Commands
{
    public class HighlightSimilarKeysToggleCommand : ToggleCommand
    {
        public HighlightSimilarKeysToggleCommand(MainWindow mainWindow)
            : base("Highlight similar keys", Properties.Settings.Default.HighlightSimilarKeys)
        {
            this.MainWindow = mainWindow;
            this.MainWindow.PropertyChanged += OnMainWindowPropertyChanged;
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        private void OnMainWindowPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "Mode":
                    this.SetCanExecute(this.MainWindow.Mode == MainWindow.DisplayMode.TreeView);
                    break;
            }
        }

        private MainWindow MainWindow;

        public override void Execute(object parameter)
        {
            Properties.Settings.Default.HighlightSimilarKeys = !Properties.Settings.Default.HighlightSimilarKeys;
            Properties.Settings.Default.Save();
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
