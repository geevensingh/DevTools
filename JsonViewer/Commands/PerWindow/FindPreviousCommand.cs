namespace JsonViewer.Commands.PerWindow
{
    using System.Diagnostics;
    using System.Windows.Input;
    using JsonViewer.Model;
    using JsonViewer.View;

    public class FindPreviousCommand : BaseCommand
    {
        public FindPreviousCommand(TabContent mainWindow)
            : base("Previous", true)
        {
            this.Tab = mainWindow;
            this.Tab.Finder.PropertyChanged += OnFinderPropertyChanged;
            this.Update();

            this.AddKeyGesture(new KeyGesture(Key.Left, ModifierKeys.Control));
            this.AddKeyGesture(new KeyGesture(Key.Left, ModifierKeys.Alt));
        }

        public override void Execute(object parameter)
        {
            this.Tab.Toolbar.FindMatchNavigator.Go(FindMatchNavigator.Direction.Backward);
        }

        private void OnFinderPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            FileLogger.Assert(sender == this.Tab.Finder);
            switch (e.PropertyName)
            {
                case "HitCount":
                    this.Update();
                    break;
            }
        }

        private void Update()
        {
            this.SetCanExecute(this.Tab.Finder.HitCount > 0);
        }
    }
}
