namespace JsonViewer.Commands.PerWindow
{
    using System.Diagnostics;
    using System.Windows.Input;
    using JsonViewer.View;

    public class FindPreviousCommand : BaseCommand
    {
        public FindPreviousCommand(MainWindow mainWindow)
            : base("Previous", true)
        {
            this.MainWindow = mainWindow;
            this.MainWindow.Finder.PropertyChanged += OnFinderPropertyChanged;
            this.Update();

            this.AddKeyGesture(new KeyGesture(Key.Left, ModifierKeys.Control));
            this.AddKeyGesture(new KeyGesture(Key.Left, ModifierKeys.Alt));
        }

        public override void Execute(object parameter)
        {
            this.MainWindow.Toolbar.FindMatchNavigator.Go(FindMatchNavigator.Direction.Backward);
        }

        private void OnFinderPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Debug.Assert(sender == this.MainWindow.Finder);
            switch (e.PropertyName)
            {
                case "HitCount":
                    this.Update();
                    break;
            }
        }

        private void Update()
        {
            this.SetCanExecute(this.MainWindow.Finder.HitCount > 0);
        }
    }
}
