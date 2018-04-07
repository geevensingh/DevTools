namespace JsonViewer.Commands.PerItem
{
    using JsonViewer.Model;
    using JsonViewer.View;

    internal class TreatAsTextCommand : BaseTreeViewDataCommand
    {
        private CustomTreeView _tree;

        public TreatAsTextCommand(CustomTreeView tree, TreeViewData data)
            : base(data, "Treat as text", data.JsonObject.CanTreatAsText)
        {
            _tree = tree;
        }

        public override async void Execute(object parameter)
        {
            JsonObject obj = this.Data.JsonObject;
            await obj.TreatAsText();
            _tree.SelectItem(obj.ViewObject);
        }
    }
}
