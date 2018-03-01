namespace JsonViewer.Commands.PerWindow
{
    public class CollapseAllCommand : BaseCommand
    {
        private MainWindow _mainWindow = null;

        public CollapseAllCommand(MainWindow mainWindow)
            : base("Collapse all")
        {
            _mainWindow = mainWindow;
            _mainWindow.PropertyChanged += OnMainWindowPropertyChanged;
            this.SetCanExecute(HasMultipleLevels(_mainWindow));
        }

        public static bool HasMultipleLevels(MainWindow mainWindow)
        {
            return HasLevel(mainWindow.RootObject, 2);
        }

        internal static bool HasLevel(JsonObject root, int depth)
        {
            if (root == null)
            {
                return false;
            }

            if (depth == 0)
            {
                return true;
            }

            foreach (JsonObject child in root.Children)
            {
                if (HasLevel(child, depth - 1))
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
