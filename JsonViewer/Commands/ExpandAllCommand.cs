namespace JsonViewer.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    internal class ExpandAllCommand : BaseCommand
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public ExpandAllCommand()
            : base("Expand All")
        {
            this.Update();
        }

        public override void Execute(object parameter)
        {
            App.Current.MainWindow.Tree.ExpandAll();
        }

        public void Update()
        {
            this.OnMainWindowChanged(App.Current);
        }

        protected override void OnMainWindowChanged(App sender)
        {
            this.SetCanExecute(CollapseAllCommand.HasMultipleLevels(sender));
        }
    }
}
