namespace JsonViewer.Commands.PerItem
{
    using System.Windows;
    using JsonViewer.View;
    using Utilities;

    internal class ExpandAllCommand : BaseTreeViewDataCommand
    {
        public ExpandAllCommand(TreeViewData data)
            : base(data, "Expand all", data.HasChildren)
        {
        }

        public override void Execute(object parameter)
        {
            this.Data.Tree.ExpandAll(this.Data);
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
