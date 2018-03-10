namespace JsonViewer.Commands
{
    using System.Windows.Input;

    public class HighlightParentsToggleCommand : ToggleCommand
    {
        public HighlightParentsToggleCommand(MainWindow mainWindow)
            : base("Highlight parents", Properties.Settings.Default.HighlightSelectedParents)
        {
            this.MainWindow = mainWindow;
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;

            this.AddKeyGesture(new KeyGesture(Key.H, ModifierKeys.Control));
        }

        public override void Execute(object parameter)
        {
            Properties.Settings.Default.HighlightSelectedParents = !Properties.Settings.Default.HighlightSelectedParents;
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
                case "HighlightSelectedParents":
                    this.IsChecked = Properties.Settings.Default.HighlightSelectedParents;
                    break;
            }
        }
    }
}
