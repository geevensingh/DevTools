﻿namespace JsonViewer.Commands.PerItem
{
    using System.Windows;
    using JsonViewer;

    internal class ExpandChildrenCommand : BaseTreeViewDataCommand
    {
        public ExpandChildrenCommand(TreeViewData data)
            : base(data, "Expand children", CanExpandChildren(data))
        {
        }

        public override void Execute(object parameter)
        {
            this.Data.Tree.ExpandChildren(this.Data);
        }

        protected override void OnDataPropertyChanged(string propertyName)
        {
            switch (propertyName)
            {
                case "AllChildren":
                    this.SetCanExecute(CanExpandChildren(this.Data));
                    break;
            }
        }

        private static bool CanExpandChildren(TreeViewData data)
        {
            foreach (TreeViewData child in data.Children)
            {
                if (child.HasChildren)
                {
                    return true;
                }
            }

            return false;
        }
    }
}