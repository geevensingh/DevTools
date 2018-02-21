namespace JsonViewer.Commands.PerWindow
{
    public class CollapseAllCommand : BaseCommand
    {
        private MainWindow _mainWindow = null;

        public CollapseAllCommand(MainWindow mainWindow)
            : base("Collapse All")
        {
            _mainWindow = mainWindow;
            _mainWindow.PropertyChanged += OnMainWindowPropertyChanged;
            this.SetCanExecute(HasMultipleLevels(_mainWindow));
        }

        public static bool HasMultipleLevels(MainWindow mainWindow)
        {
            RootObject root = (RootObject)mainWindow.RootObject;
            if (root == null)
            {
                return false;
            }

            foreach (JsonObject child in root.Children)
            {
                if (child.HasChildren)
                {
                    return true;
                }
            }

            return false;
        }

        public override void Execute(object parameter)
        {
            _mainWindow.Tree.CollapseAll();
        }

        private void OnMainWindowPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "RootObject":
                    this.SetCanExecute(HasMultipleLevels(_mainWindow));
                    break;
            }
        }
    }
}
