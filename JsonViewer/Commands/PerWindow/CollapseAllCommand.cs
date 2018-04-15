namespace JsonViewer.Commands.PerWindow
{
    using JsonViewer.View;

    public class CollapseAllCommand : BaseCommand
    {
        public CollapseAllCommand(TabContent tab)
            : base("Collapse all")
        {
            this.ForceVisibility = System.Windows.Visibility.Visible;
            this.Tab = tab;
            this.Tab.Tree.PropertyChanged += OnTreePropertyChanged;
            this.Update();
        }

        public override void Execute(object parameter)
        {
            this.Tab.Tree.CollapseAll();
        }

        protected override void OnTabPropertyChanged(string propertyName)
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
