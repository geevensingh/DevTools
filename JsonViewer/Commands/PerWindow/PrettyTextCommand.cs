namespace JsonViewer.Commands.PerWindow
{
    using System.Diagnostics;
    using JsonViewer.Model;
    using JsonViewer.View;

    public class PrettyTextCommand : BaseCommand
    {
        public PrettyTextCommand(TabContent tab)
            : base("Pretty-ify text")
        {
            this.ForceVisibility = System.Windows.Visibility.Visible;
            this.Tab = tab;
            this.Update();
        }

        public override void Execute(object parameter)
        {
            FileLogger.Assert(this.Tab.RootObject != null);
            this.Tab.Raw_TextBox.Text = this.Tab.RootObject?.PrettyValueString;
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
            this.SetCanExecute(this.Tab.RootObject != null);
        }
    }
}
