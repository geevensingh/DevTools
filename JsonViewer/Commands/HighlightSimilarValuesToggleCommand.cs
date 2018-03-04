namespace JsonViewer.Commands
{
    public class HighlightSimilarValuesToggleCommand : ToggleCommand
    {
        public HighlightSimilarValuesToggleCommand()
            : base("Highlight similar values", Properties.Settings.Default.HighlightSimilarValues)
        {
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        public override void Execute(object parameter)
        {
            Properties.Settings.Default.HighlightSimilarValues = !Properties.Settings.Default.HighlightSimilarValues;
            Properties.Settings.Default.Save();
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
