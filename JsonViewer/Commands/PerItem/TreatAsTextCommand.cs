namespace JsonViewer.Commands.PerItem
{
    using System.Windows;
    using JsonViewer;

    internal class TreatAsTextCommand : BaseTreeViewDataCommand
    {
        public TreatAsTextCommand(TreeViewData data)
            : base(data, "Treat as Text", data.JsonObject.CanTreatAsText)
        {
        }

        public override void Execute(object parameter)
        {
            this.Data.JsonObject.TreatAsText();
        }
    }
}
