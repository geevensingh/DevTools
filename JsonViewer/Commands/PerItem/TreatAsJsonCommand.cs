namespace JsonViewer.Commands.PerItem
{
    using System.Windows.Controls;
    using JsonViewer.Model;
    using JsonViewer.View;

    internal class TreatAsJsonCommand : BaseTreeViewDataCommand
    {
        private ListView _tree;

        public TreatAsJsonCommand(ListView tree, TreeViewData data)
            : base(data, "Treat as Json", data.JsonObject.CanTreatAsJson)
        {
            _tree = tree;
        }

        public override void Execute(object parameter)
        {
            JsonObject obj = this.Data.JsonObject;
            obj.TreatAsJson();
            //_tree.SelectItem(obj.ViewObject).IsExpanded = true;
        }
    }
}
