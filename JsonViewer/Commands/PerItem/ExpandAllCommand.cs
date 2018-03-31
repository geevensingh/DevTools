namespace JsonViewer.Commands.PerItem
{
    using JsonViewer.View;

    public class ExpandAllCommand : BaseTreeViewDataCommand
    {
        public ExpandAllCommand(TreeViewData data)
            : base(data, "Expand all", data.HasChildren)
        {
        }

        public override void Execute(object parameter)
        {
            //this.Data.Tree.SelectItem(this.Data);
            //this.Data.Tree.ExpandSubtree(this.Data, int.MaxValue);
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
