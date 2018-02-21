namespace JsonViewer.Commands.PerWindow
{
    public class ExpandAllCommand : BaseCommand
    {
        private MainWindow _mainWindow = null;

        public ExpandAllCommand(MainWindow mainWindow)
            : base("Expand All")
        {
            _mainWindow = mainWindow;
            _mainWindow.PropertyChanged += OnMainWindowPropertyChanged;
            this.SetCanExecute(CollapseAllCommand.HasMultipleLevels(_mainWindow));
        }

        public override void Execute(object parameter)
        {
            _mainWindow.Tree.ExpandAll();
        }

        private void OnMainWindowPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "RootObject":
                    this.SetCanExecute(CollapseAllCommand.HasMultipleLevels(_mainWindow));
                    break;
            }
        }
    }
}
