namespace JsonViewer.Commands.PerWindow
{
    using System.Diagnostics;
    using System.Windows.Input;
    using JsonViewer.View;

    public class FindPreviousCommand : BaseCommand
    {
        private MainWindow _mainWindow = null;

        public FindPreviousCommand(MainWindow mainWindow)
            : base("Previous", true)
        {
            _mainWindow = mainWindow;
            _mainWindow.PropertyChanged += OnMainWindowPropertyChanged;
            _mainWindow.Finder.PropertyChanged += OnFinderPropertyChanged;
            this.Update();

            this.AddKeyGesture(new KeyGesture(Key.Left, ModifierKeys.Control));
            this.AddKeyGesture(new KeyGesture(Key.Left, ModifierKeys.Alt));
        }

        private void OnMainWindowPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "Mode":
                    this.Update();
                    break;
            }
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
                    this.Update();
                    break;
            }
        }

        private void Update()
        {
            this.SetCanExecute(_mainWindow.Mode == MainWindow.DisplayMode.TreeView && _mainWindow.Finder.HitCount > 0);
        }
    }
}
