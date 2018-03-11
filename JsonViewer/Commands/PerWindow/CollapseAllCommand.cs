namespace JsonViewer.Commands.PerWindow
{
    using JsonViewer.View;

    public class CollapseAllCommand : BaseCommand
    {
        public CollapseAllCommand(MainWindow mainWindow)
            : base("Collapse all")
        {
            this.MainWindow = mainWindow;

            this.MainWindow.Tree.PropertyChanged += OnTreePropertyChanged;
            this.Update();
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
            this.SetCanExecute(!this.MainWindow.Tree.IsWaiting && this.MainWindow.RootObject != null && this.MainWindow.RootObject.HasLevel(2));
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
