namespace JsonViewer.Commands.PerItem
{
    using System.Windows;
    using JsonViewer.View;

    internal class TreatAsTextCommand : BaseTreeViewDataCommand
    {
        private CustomTreeView _tree;

        public TreatAsTextCommand(CustomTreeView tree, TreeViewData data)
            : base(data, "Treat as Text", data.JsonObject.CanTreatAsText)
        {
            _tree = tree;
        }

        public override void Execute(object parameter)
        {
            JsonObject obj = this.Data.JsonObject;
            obj.TreatAsText();
            _tree.SelectItem(obj.ViewObject);
        }
    }
}
