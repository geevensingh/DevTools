namespace JsonViewer.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    internal class ExpandAllCommand : BaseCommand
    {
        public ExpandAllCommand()
        {
            this.Update();
            App.Current.MainWindowChanged += OnMainWindowChanged;
        }

        public override void Execute(object parameter)
        {
            App.Current.MainWindow.Tree.ExpandAll();
        }

        public void Update()
        {
            this.OnMainWindowChanged(App.Current);
        }

        private void OnMainWindowChanged(App sender)
        {
            this.SetCanExecute(CollapseAllCommand.HasMultipleLevels(sender));
        }
    }
}
