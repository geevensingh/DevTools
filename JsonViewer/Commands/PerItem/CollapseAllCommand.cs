namespace JsonViewer.Commands.PerItem
{
    using JsonViewer.View;

    internal class CollapseAllCommand : BaseTreeViewDataCommand
    {
        public CollapseAllCommand(TreeViewData data)
            : base(data, "Collapse all", data.HasChildren)
        {
        }

        public override void Execute(object parameter)
        {
            //this.Data.Tree.SelectItem(this.Data);
            //this.Data.Tree.CollapseSubtree(this.Data);
        }

        protected override void OnDataPropertyChanged(string propertyName)
        {
            switch (propertyName)
            {
                case "AllChildren":
                    this.SetCanExecute(this.Data.HasChildren);
                    break;
            }
        }
    }
}
