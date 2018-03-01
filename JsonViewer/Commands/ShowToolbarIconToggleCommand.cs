namespace JsonViewer.Commands
{
    public class ShowToolbarIconToggleCommand : ToggleCommand
    {
        public ShowToolbarIconToggleCommand()
            : base("Show toolbar icons", Properties.Settings.Default.MainWindowToolbarIconVisible)
        {
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        public override void Execute(object parameter)
        {
            bool newValue = !Properties.Settings.Default.MainWindowToolbarIconVisible;
            if (!newValue)
            {
                Properties.Settings.Default.MainWindowToolbarTextVisible = true;
            }

            Properties.Settings.Default.MainWindowToolbarIconVisible = newValue;
            Properties.Settings.Default.Save();
        }

        private void OnSettingsPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "MainWindowToolbarIconVisible":
                    this.IsChecked = Properties.Settings.Default.MainWindowToolbarIconVisible;
                    break;
            }
        }
    }
}
