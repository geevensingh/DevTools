namespace JsonViewer
{
    using System;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;

    internal class CustomTreeView : TreeView
    {
        private TreeViewData _selected = null;

        public void ExpandChildren(TreeViewData data)
        {
            TreeViewItem item = GetItem(data);
            item.IsExpanded = true;
            item.UpdateLayout();
            item.ItemContainerGenerator.GenerateBatches().Dispose();
            Debug.Assert(item.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated);
            foreach (TreeViewData child in data.Children)
            {
                if (child.HasChildren)
                {
                    this.Dispatcher.BeginInvoke(
                        new Action(() =>
                        {
                            TreeViewItem asyncChildItem = (TreeViewItem)item.ItemContainerGenerator.ContainerFromItem(child);
                            Debug.Assert(asyncChildItem != null);
                            if (asyncChildItem != null)
                            {
                                asyncChildItem.IsExpanded = true;
                                asyncChildItem.UpdateLayout();
                            }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        public void ExpandSubtree(TreeViewData data)
        {
            this.ExpandSubtree(GetItemContainerGenerator(data.Parent), data);
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
            this.CollapseSubtree(this.GetItemContainerGenerator(data.Parent), data);
        }

        public void CollapseAll()
        {
            foreach (TreeViewData data in this.Items)
            {
                Debug.Assert(data.Parent == null);
                this.CollapseSubtree(this.ItemContainerGenerator, data);
            }
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            Config config = Config.This;
            this.Foreground = config.GetBrush(ConfigValue.TreeViewForeground);
            this.Resources[SystemColors.HighlightBrushKey] = config.GetBrush(ConfigValue.TreeViewHighlightBrushKey);
            this.Resources[SystemColors.HighlightTextBrushKey] = config.GetBrush(ConfigValue.TreeViewHighlightTextBrushKey);
            this.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = config.GetBrush(ConfigValue.TreeViewInactiveSelectionHighlightBrushKey);
            this.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = config.GetBrush(ConfigValue.TreeViewInactiveSelectionHighlightTextBrushKey);
        }

        protected override DependencyObject GetContainerForItemOverride()
        {
            return new StretchingTreeViewItem();
        }

        protected override bool IsItemItsOwnContainerOverride(object item)
        {
            return item is StretchingTreeViewItem;
        }

        protected override void OnSelectedItemChanged(RoutedPropertyChangedEventArgs<object> e)
        {
            base.OnSelectedItemChanged(e);
            if (_selected != null)
            {
                _selected.IsSelected = false;
            }

            _selected = this.SelectedValue as TreeViewData;
            if (_selected != null)
            {
                _selected.IsSelected = true;
            }
        }

        private ItemContainerGenerator GetItemContainerGenerator(TreeViewData data)
        {
            if (data == null)
            {
                return this.ItemContainerGenerator;
            }

            ItemContainerGenerator generator = GetItemContainerGenerator(data.Parent);
            TreeViewItem treeViewItem = generator.ContainerFromItem(data) as TreeViewItem;
            return treeViewItem.ItemContainerGenerator;
        }

        private TreeViewItem GetItem(TreeViewData data)
        {
            return GetItemContainerGenerator(data.Parent).ContainerFromItem(data) as TreeViewItem;
        }

        private void ExpandSubtree(ItemContainerGenerator parentContainerGenerator, TreeViewData data)
        {
            Debug.Assert(parentContainerGenerator.ContainerFromItem(data) != null);
            this.ExpandSubtree(parentContainerGenerator.ContainerFromItem(data) as TreeViewItem);
        }

        private void CollapseSubtree(ItemContainerGenerator parentContainerGenerator, TreeViewData data)
        {
            TreeViewItem tvi = parentContainerGenerator.ContainerFromItem(data) as TreeViewItem;
            if (tvi != null)
            {
                tvi.IsExpanded = false;
                foreach (TreeViewData child in data.Children)
                {
                    this.CollapseSubtree(tvi.ItemContainerGenerator, child);
                }
            }
        }

        public TreeViewItem ExpandToItem(TreeViewData treeViewData)
        {
            TreeViewItem parentItem = null;
            if (treeViewData.Parent != null)
            {
                parentItem = ExpandToItem(treeViewData.Parent);
            }

            TreeViewItem item = null;
            if (parentItem == null)
            {
                Debug.Assert(treeViewData.Parent == null);
                item = GetItem(treeViewData);
            }
            else
            {
                bool isExpanded = parentItem.IsExpanded;
                if (!isExpanded)
                {
                    parentItem.IsExpanded = true;
                    parentItem.UpdateLayout();
                }

                item = (TreeViewItem)parentItem.ItemContainerGenerator.ContainerFromItem(treeViewData);
            }

            Debug.Assert(item != null);
            Debug.Assert(item.DataContext == treeViewData);

            item.IsSelected = true;
            item.BringIntoView(new Rect(0, -50, item.ActualWidth, 100 + item.ActualHeight));
            return item;
        }

        public TreeViewItem SelectItem(TreeViewData treeViewData)
        {
            TreeViewItem item = ExpandToItem(treeViewData);
            item.IsSelected = true;

            Debug.Assert(!double.IsNaN(item.ActualWidth));
            Debug.Assert(!double.IsNaN(item.ActualHeight));
            item.BringIntoView(new Rect(0, -50, item.ActualWidth, 100 + item.ActualHeight));

            return item;
        }
    }
}
