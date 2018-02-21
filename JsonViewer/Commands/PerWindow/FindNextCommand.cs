namespace JsonViewer.Commands.PerWindow
{
    using System.Diagnostics;
    using JsonViewer.View;

    public class FindNextCommand : BaseCommand
    {
        private MainWindow _mainWindow = null;

        public FindNextCommand(MainWindow mainWindow)
            : base("Next", true)
        {
            _mainWindow = mainWindow;
            _mainWindow.Finder.PropertyChanged += OnFinderPropertyChanged;
            this.SetCanExecute(_mainWindow.Finder.HitCount > 0);
        }

        public override void Execute(object parameter)
        {
            _mainWindow.Toolbar.FindMatchNavigator.Go(FindMatchNavigator.Direction.Forward);
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
