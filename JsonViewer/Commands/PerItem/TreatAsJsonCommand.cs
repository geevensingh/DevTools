namespace JsonViewer.Commands.PerItem
{
    using System.Windows;
    using JsonViewer;

    internal class TreatAsJsonCommand : BaseTreeViewDataCommand
    {
        public TreatAsJsonCommand(TreeViewData data)
            : base(data, "Treat as Json", data.JsonObject.CanTreatAsJson)
        {
        }

        public override void Execute(object parameter)
        {
            this.Data.JsonObject.TreatAsJson();
        }
    }
}
