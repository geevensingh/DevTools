namespace JsonViewer.Commands.PerWindow
{
    using System.Windows;

    public class CollapseAllCommand : BaseCommand
    {
        public CollapseAllCommand(MainWindow mainWindow)
            : base("Collapse all")
        {
            this.MainWindow = mainWindow;

            this.MainWindow.Tree.PropertyChanged += OnTreePropertyChanged;
            this.Update();
        }

        public static bool HasMultipleLevels(MainWindow mainWindow)
        {
            return HasLevel(mainWindow.RootObject, 2);
        }

        public static bool HasLevel(JsonObject root, int depth)
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
            this.MainWindow.Tree.CollapseAll();
        }

        protected override void OnMainWindowPropertyChanged(string propertyName)
        {
            switch (propertyName)
            {
                case "RootObject":
                    this.Update();
                    break;
            }
        }

        private void Update()
        {
            this.SetCanExecute(!this.MainWindow.Tree.IsWaiting && HasMultipleLevels(this.MainWindow));
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
    }
}
