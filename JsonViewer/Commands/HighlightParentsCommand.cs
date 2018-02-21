namespace JsonViewer.Commands
{
    using JsonViewer;

    public class HighlightParentsCommand : BaseCommand
    {
        public HighlightParentsCommand()
            : base("Highlight Selected Item Parents", true)
        {
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        public bool IsChecked { get => Properties.Settings.Default.HighlightSelectedParents; }

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
                    this.FirePropertyChanged("IsChecked");
                    break;
            }
        }
    }
}
