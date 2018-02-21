namespace JsonViewer.Commands.PerWindow
{
    using System.Diagnostics;
    using JsonViewer.View;

    public class FindPreviousCommand : BaseCommand
    {
        private MainWindow _mainWindow = null;

        public FindPreviousCommand(MainWindow mainWindow)
            : base("Previous", true)
        {
            _mainWindow = mainWindow;
            _mainWindow.Finder.PropertyChanged += OnFinderPropertyChanged;
            this.SetCanExecute(_mainWindow.Finder.HitCount > 0);
        }

        public override void Execute(object parameter)
        {
            _mainWindow.Toolbar.FindMatchNavigator.Go(FindMatchNavigator.Direction.Backward);
        }

        private void OnFinderPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Debug.Assert(sender == _mainWindow.Finder);
            switch (e.PropertyName)
            {
                case "HitCount":
                    this.SetCanExecute(_mainWindow.Finder.HitCount > 0);
                    break;
            }
        }
    }
}
