namespace JsonViewer.Commands.PerWindow
{
    public class ExpandAllCommand : BaseCommand
    {
        private MainWindow _mainWindow = null;

        public ExpandAllCommand(MainWindow mainWindow)
            : base("Expand all")
        {
            _mainWindow = mainWindow;
            _mainWindow.PropertyChanged += OnMainWindowPropertyChanged;

            _mainWindow.Tree.PropertyChanged += OnTreePropertyChanged;
            Update();
        }

        public override void Execute(object parameter)
        {
            _mainWindow.Tree.ExpandAll();
        }

        private void Update()
        {
            this.SetCanExecute(_mainWindow.Mode == MainWindow.DisplayMode.TreeView && !_mainWindow.Tree.IsWaiting && CollapseAllCommand.HasMultipleLevels(_mainWindow));
        }

        private void OnTreePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "IsWaiting":
                    this.Update();
                    break;
            }
        }

        private void OnMainWindowPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "Mode":
                case "RootObject":
                    this.Update();
                    break;
            }
        }
    }
}
