namespace JsonViewer.Commands.PerItem
{
    using System.Windows.Controls;
    using JsonViewer.Model;
    using JsonViewer.View;

    internal class TreatAsTextCommand : BaseTreeViewDataCommand
    {
        private ListView _tree;

        public TreatAsTextCommand(ListView tree, TreeViewData data)
            : base(data, "Treat as text", data.JsonObject.CanTreatAsText)
        {
            _tree = tree;
        }

        public override void Execute(object parameter)
        {
            JsonObject obj = this.Data.JsonObject;
            obj.TreatAsText();
            //_tree.SelectItem(obj.ViewObject);
        }
    }
}
