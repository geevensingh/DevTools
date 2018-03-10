namespace JsonViewer.Commands.PerWindow
{
    using System.Windows;

    public class ExpandAllCommand : BaseCommand
    {
        public ExpandAllCommand(MainWindow mainWindow)
            : base("Expand all")
        {
            this.MainWindow = mainWindow;

            this.MainWindow.Tree.PropertyChanged += OnTreePropertyChanged;
            Update();
        }

        public override void Execute(object parameter)
        {
            this.MainWindow.Tree.ExpandAll();
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
            this.SetCanExecute(!this.MainWindow.Tree.IsWaiting && CollapseAllCommand.HasMultipleLevels(this.MainWindow));
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
