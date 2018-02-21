namespace JsonViewer.Commands
{
    public class ShowToolbarIconCommand : BaseCommand
    {
        public ShowToolbarIconCommand()
            : base("Show toolbar icons", true)
        {
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        public bool IsChecked { get => Properties.Settings.Default.MainWindowToolbarIconVisible; }

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
                    this.FirePropertyChanged("IsChecked");
                    break;
            }
        }
    }
}
