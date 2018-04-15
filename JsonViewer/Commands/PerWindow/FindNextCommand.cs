namespace JsonViewer.Commands.PerWindow
{
    using System.Diagnostics;
    using System.Windows.Input;
    using JsonViewer.Model;
    using JsonViewer.View;

    public class FindNextCommand : BaseCommand
    {
        public FindNextCommand(TabContent tab)
            : base("Next", true)
        {
            this.Tab = tab;
            this.Tab.Finder.PropertyChanged += OnFinderPropertyChanged;
            this.Update();

            this.AddKeyGesture(new KeyGesture(Key.Right, ModifierKeys.Control));
            this.AddKeyGesture(new KeyGesture(Key.Right, ModifierKeys.Alt));
        }

        public override void Execute(object parameter)
        {
            this.Tab.Toolbar.FindMatchNavigator.Go(FindMatchNavigator.Direction.Forward);
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
