using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace JsonViewer
{
    internal class CustomTreeView : TreeView
    {
        protected override DependencyObject GetContainerForItemOverride()
        {
            return new StretchingTreeViewItem();
        }
        protected override bool IsItemItsOwnContainerOverride(object item)
        {
            return item is StretchingTreeViewItem;
        }

        private ItemContainerGenerator GetParentItemContainerGenerator(TreeViewData data)
        {
            var parentList = data.ParentList;
            var generator = this.ItemContainerGenerator;
            for (int ii = 0; ii < parentList.Count; ii++)
            {
                generator = (generator.ContainerFromItem(parentList[ii]) as TreeViewItem).ItemContainerGenerator;
            }
            return generator;
        }

        public void ExpandSubtree(TreeViewData data)
        {
            this.ExpandSubtree(GetParentItemContainerGenerator(data), data);
        }
        private void ExpandSubtree(ItemContainerGenerator parentContainerGenerator, TreeViewData data)
        {
            Debug.Assert(parentContainerGenerator.ContainerFromItem(data) != null);
            base.ExpandSubtree(parentContainerGenerator.ContainerFromItem(data) as TreeViewItem);
        }

        public void ExpandAll()
        {
            foreach (TreeViewData data in this.Items)
            {
                Debug.Assert(data.Parent == null);
                this.ExpandSubtree(this.ItemContainerGenerator, data);
            }
        }

        public void CollapseSubtree(TreeViewData data)
        {
            this.CollapseSubtree(this.GetParentItemContainerGenerator(data), data);
        }

        public void CollapseAll()
        {
            foreach (TreeViewData data in this.Items)
            {
                Debug.Assert(data.Parent == null);
                this.CollapseSubtree(this.ItemContainerGenerator, data);
            }
        }

        private void CollapseSubtree(ItemContainerGenerator parentContainerGenerator, TreeViewData data)
        {
            TreeViewItem tvi = (parentContainerGenerator.ContainerFromItem(data) as TreeViewItem);
            if (tvi != null)
            {
                tvi.IsExpanded = false;
                foreach (TreeViewData child in data.Children)
                {
                    this.CollapseSubtree(tvi.ItemContainerGenerator, child);
                }
            }
        }
    }
}
