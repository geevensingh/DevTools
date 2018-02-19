namespace JsonViewer.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using JsonViewer.View;

    internal class FindPreviousCommand : BaseCommand
    {
        public FindPreviousCommand()
            : base("Previous", true)
        {
        }

        public override void Execute(object parameter)
        {
            App.Current.MainWindow.Toolbar.FindMatchNavigator.Go(FindMatchNavigator.Direction.Backward);
        }

        protected override void OnMainWindowChanged(App sender)
        {
            this.SetCanExecute(App.Current.MainWindow.Finder.HitCount > 0);
        }
    }
}
