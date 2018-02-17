namespace JsonViewer.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    internal class HideFindCommand : BaseCommand
    {
        public HideFindCommand()
            : base("Hide Find Window")
        {
            App.Current.MainWindowChanged += OnMainWindowChanged;
        }

        public override void Execute(object parameter)
        {
            App.Current.MainWindow.Finder.HideWindow();
        }

        private void OnMainWindowChanged(App sender)
        {
            this.SetCanExecute(sender.MainWindow.Finder.HasWindow);
        }
    }
}
