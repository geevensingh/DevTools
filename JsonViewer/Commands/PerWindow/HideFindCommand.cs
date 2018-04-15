namespace JsonViewer.Commands.PerWindow
{
    using System.Diagnostics;
    using System.Windows.Input;
    using JsonViewer.Model;
    using JsonViewer.View;

    public class HideFindCommand : BaseCommand
    {
        private Finder _finder = null;

        public HideFindCommand(TabContent tab)
            : base("Hide find window")
        {
            _finder = tab.Finder;
            _finder.PropertyChanged += OnFinderPropertyChanged;
            this.SetCanExecute(_finder.HasWindow);

            this.AddKeyGesture(new KeyGesture(Key.Escape));
        }

        public override void Execute(object parameter)
        {
            _finder.HideWindow();
        }

        private void OnFinderPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            FileLogger.Assert(sender == _finder);
            if (e.PropertyName == "HasWindow")
            {
                this.SetCanExecute(_finder.HasWindow);
            }
        }
    }
}
