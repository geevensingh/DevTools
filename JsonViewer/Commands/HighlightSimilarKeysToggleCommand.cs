namespace JsonViewer.Commands
{
    public class HighlightSimilarKeysToggleCommand : ToggleCommand
    {
        public HighlightSimilarKeysToggleCommand()
            : base("Highlight similar keys", Properties.Settings.Default.HighlightSimilarKeys)
        {
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

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
