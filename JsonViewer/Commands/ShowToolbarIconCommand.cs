namespace JsonViewer.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    internal class ShowToolbarIconCommand : BaseCommand
    {
        public ShowToolbarIconCommand()
            : base("Show toolbar icons", true)
        {
        }

        public override void Execute(object parameter)
        {
            bool newValue = !Properties.Settings.Default.MainWindowToolbarIconVisible;
            if (!newValue)
            {
                Properties.Settings.Default.MainWindowToolbarTextVisible = true;
            }

            Properties.Settings.Default.MainWindowToolbarIconVisible = newValue;
            Properties.Settings.Default.Save();
        }
    }
}
