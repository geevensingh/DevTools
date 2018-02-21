namespace JsonViewer.Commands.PerItem
{
    using System.ComponentModel;
    using System.Windows;
    using JsonViewer;

    internal class CollapseAllCommand : BaseTreeViewDataCommand
    {
        public CollapseAllCommand(TreeViewData data)
            : base(data, "Collapse All", data.HasChildren)
        {
        }

        public override void Execute(object parameter)
        {
            this.Data.Tree.CollapseSubtree(this.Data);
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
