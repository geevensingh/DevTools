namespace JsonViewer.Commands.PerItem
{
    using JsonViewer.Model;
    using JsonViewer.View;

    internal class TreatAsJsonCommand : BaseTreeViewDataCommand
    {
        private CustomTreeView _tree;

        public TreatAsJsonCommand(CustomTreeView tree, TreeViewData data)
            : base(data, "Treat as Json", false)
        {
            _tree = tree;
            data.JsonObject.CanTreatAsJson().ContinueWith((canTreatAsJsonTask) =>
            {
                this.SetCanExecute(canTreatAsJsonTask.Result);
            });
        }

        public override async void Execute(object parameter)
        {
            JsonObject obj = this.Data.JsonObject;
            await obj.TreatAsJson();
            _tree.SelectItem(obj.ViewObject).IsExpanded = true;
        }
    }
}
