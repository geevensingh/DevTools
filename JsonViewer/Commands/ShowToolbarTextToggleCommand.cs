namespace JsonViewer.Commands
{
    public class ShowToolbarTextToggleCommand : ToggleCommand
    {
        public ShowToolbarTextToggleCommand()
            : base("Show toolbar text", Properties.Settings.Default.MainWindowToolbarTextVisible)
        {
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

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
                    this.IsChecked = Properties.Settings.Default.MainWindowToolbarTextVisible;
                    break;
            }
        }
    }
}
