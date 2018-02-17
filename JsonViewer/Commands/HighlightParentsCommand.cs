namespace JsonViewer.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    internal class HighlightParentsCommand : BaseCommand
    {
        public HighlightParentsCommand()
            : base("Highlight Selected Item Parents", true)
        {
        }

        public override void Execute(object parameter)
        {
            bool newValue = !Properties.Settings.Default.HighlightSelectedParents;
            Properties.Settings.Default.HighlightSelectedParents = newValue;
            Properties.Settings.Default.Save();

            MainWindow mainWindow = App.Current.MainWindow;
            mainWindow.Toolbar.HighlightParentsButton.IsChecked = newValue;
            TreeViewData selected = mainWindow.Tree.SelectedValue as TreeViewData;
            if (selected != null)
            {
                selected.IsSelected = selected.IsSelected;
            }
        }
    }
}
