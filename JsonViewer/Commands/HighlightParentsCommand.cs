namespace JsonViewer.Commands
{
    using JsonViewer;

    public class HighlightParentsCommand : ToggleCommand
    {
        public HighlightParentsCommand()
            : base("Highlight Selected Item Parents", Properties.Settings.Default.HighlightSelectedParents)
        {
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        public override void Execute(object parameter)
        {
            Properties.Settings.Default.HighlightSelectedParents = !Properties.Settings.Default.HighlightSelectedParents;
            Properties.Settings.Default.Save();
        }

        private void OnSettingsPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "HighlightSelectedParents":
                    this.IsChecked = Properties.Settings.Default.HighlightSelectedParents;
                    break;
            }
        }
    }
}
