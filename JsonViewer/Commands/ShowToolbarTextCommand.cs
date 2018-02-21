namespace JsonViewer.Commands
{
    public class ShowToolbarTextCommand : BaseCommand
    {
        public ShowToolbarTextCommand()
            : base("Show toolbar text", true)
        {
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        public bool IsChecked { get => Properties.Settings.Default.MainWindowToolbarTextVisible; }

        public override void Execute(object parameter)
        {
            bool newValue = !Properties.Settings.Default.MainWindowToolbarTextVisible;
            if (!newValue)
            {
                Properties.Settings.Default.MainWindowToolbarIconVisible = true;
            }

            Properties.Settings.Default.MainWindowToolbarTextVisible = newValue;
            Properties.Settings.Default.Save();
        }

        private void OnSettingsPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "MainWindowToolbarTextVisible":
                    this.FirePropertyChanged("IsChecked");
                    break;
            }
        }
    }
}
