namespace JsonViewer.Commands.PerWindow
{
    using System.Diagnostics;

    public class HideFindCommand : BaseCommand
    {
        private Finder _finder = null;

        public HideFindCommand(MainWindow mainWindow)
            : base("Hide Find Window")
        {
            _finder = mainWindow.Finder;
            _finder.PropertyChanged += OnFinderPropertyChanged;
            this.SetCanExecute(_finder.HasWindow);
        }

        public override void Execute(object parameter)
        {
            _finder.HideWindow();
        }

        private void OnFinderPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Debug.Assert(sender == _finder);
            if (e.PropertyName == "HasWindow")
            {
                this.SetCanExecute(_finder.HasWindow);
            }
        }
    }
}
