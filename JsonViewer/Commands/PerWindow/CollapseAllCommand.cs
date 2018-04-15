namespace JsonViewer.Commands.PerWindow
{
    using JsonViewer.View;

    public class CollapseAllCommand : BaseCommand
    {
        public CollapseAllCommand(TabContent mainWindow)
            : base("Collapse all")
        {
            this.ForceVisibility = System.Windows.Visibility.Visible;
            this.Tab = mainWindow;
            this.Tab.Tree.PropertyChanged += OnTreePropertyChanged;
            this.Update();
        }

        public override void Execute(object parameter)
        {
            this.Tab.Tree.CollapseAll();
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
            this.SetCanExecute(!this.Tab.Tree.IsWaiting && this.Tab.RootObject != null && this.Tab.RootObject.HasLevel(2));
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
