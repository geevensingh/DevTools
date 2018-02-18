namespace JsonViewer.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    internal class CollapseAllCommand : BaseCommand
    {
        public CollapseAllCommand()
            : base("Collapse All")
        {
            this.Update();
            App.Current.MainWindowChanged += OnMainWindowChanged;
        }

        public static bool HasMultipleLevels(App app)
        {
            RootObject root = (RootObject)app.MainWindow.RootObject;
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
            App.Current.MainWindow.Tree.CollapseAll();
        }

        public void Update()
        {
            this.OnMainWindowChanged(App.Current);
        }

        private void OnMainWindowChanged(App sender)
        {
            this.SetCanExecute(HasMultipleLevels(sender));
        }
    }
}
