namespace JsonViewer.Commands.PerWindow
{
    using System.Diagnostics;
    using JsonViewer.Model;
    using JsonViewer.View;

    public class PrettyTextCommand : BaseCommand
    {
        public PrettyTextCommand(TabContent mainWindow)
            : base("Pretty-ify text")
        {
            this.ForceVisibility = System.Windows.Visibility.Visible;
            this.Tab = mainWindow;
            this.Update();
        }

        public override void Execute(object parameter)
        {
            FileLogger.Assert(this.Tab.RootObject != null);
            this.Tab.Raw_TextBox.Text = this.Tab.RootObject?.PrettyValueString;
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
            this.SetCanExecute(this.Tab.RootObject != null);
        }
    }
}
