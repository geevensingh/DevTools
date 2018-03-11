namespace JsonViewer.Commands.PerItem
{
    using JsonViewer.Model;
    using JsonViewer.View;

    internal class TreatAsJsonCommand : BaseTreeViewDataCommand
    {
        private CustomTreeView _tree;

        public TreatAsJsonCommand(CustomTreeView tree, TreeViewData data)
            : base(data, "Treat as Json", data.JsonObject.CanTreatAsJson)
        {
            _tree = tree;
        }

        public override void Execute(object parameter)
        {
            JsonObject obj = this.Data.JsonObject;
            obj.TreatAsJson();
            _tree.SelectItem(obj.ViewObject).IsExpanded = true;
        }
    }
}
