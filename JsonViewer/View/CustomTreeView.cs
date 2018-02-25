namespace JsonViewer.View
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using Utilities;

    internal class CustomTreeView : TreeView, INotifyPropertyChanged
    {
        private TreeViewData _selected = null;
        private int? _selectedIndex = null;

        public CustomTreeView()
        {
            CommandBinding copyCommandBinding = new CommandBinding
            {
                Command = ApplicationCommands.Copy
            };
            copyCommandBinding.Executed += OnCopyCommandExecuted;
            copyCommandBinding.CanExecute += OnCopyCommandCanExecute;
            this.CommandBindings.Add(copyCommandBinding);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public int? SelectedIndex { get => _selectedIndex; }

        public void ExpandChildren(TreeViewData data)
        {
            this.ExpandSubtree(data, 1).Forget();
        }

        public void ExpandAll()
        {
            foreach (TreeViewData child in this.Items)
            {
                this.ExpandSubtree(child, int.MaxValue).Forget();
            }
        }

        public void ExpandAll(TreeViewData data)
        {
            this.ExpandSubtree(data, int.MaxValue).Forget();
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

        public TreeViewItem GetItem(TreeViewData data)
        {
            return GetItemContainerGenerator(data.Parent).ContainerFromItem(data) as TreeViewItem;
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
            TreeViewData newSelected = this.SelectedValue as TreeViewData;
            if (newSelected == _selected)
            {
                return;
            }

            if (_selected != null)
            {
                _selected.IsSelected = false;
            }

            int? newSelectedIndex = null;
            _selected = newSelected;
            if (_selected != null)
            {
                _selected.IsSelected = true;
                newSelectedIndex = _selected.JsonObject.OverallIndex;
                Debug.Assert(newSelectedIndex >= 0);
            }

            NotifyPropertyChanged.SetValue(ref _selectedIndex, newSelectedIndex, "SelectedIndex", this, this.PropertyChanged);
        }

        private void OnCopyCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            Debug.Assert(sender == this);
            e.CanExecute = this.SelectedItem as TreeViewData != null;
        }

        private void OnCopyCommandExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            Debug.Assert(sender == this);
            Clipboard.SetText((this.SelectedItem as TreeViewData)?.Value);
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

        private void CollapseSubtree(ItemContainerGenerator parentContainerGenerator, TreeViewData data)
        {
            if (parentContainerGenerator.ContainerFromItem(data) is TreeViewItem tvi)
            {
                tvi.IsExpanded = false;
                foreach (TreeViewData child in data.Children)
                {
                    this.CollapseSubtree(tvi.ItemContainerGenerator, child);
                }
            }
        }

        private async Task ExpandSubtree(TreeViewData data, int depth)
        {
            using (new WaitCursor())
            {
                await this.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        TreeViewItem item = GetItem(data);
                        item.IsExpanded = true;
                        item.UpdateLayout();
                        item.ItemContainerGenerator.GenerateBatches().Dispose();
                        Debug.Assert(item.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated);
                    }), System.Windows.Threading.DispatcherPriority.Background).Task;

                if (depth > 0)
                {
                    foreach (TreeViewData child in data.Children)
                    {
                        if (child.HasChildren)
                        {
                            await this.Dispatcher.BeginInvoke(
                                new Action(async () =>
                                {
                                    TreeViewItem item = GetItem(data);
                                    TreeViewItem asyncChildItem = (TreeViewItem)item.ItemContainerGenerator.ContainerFromItem(child);
                                    Debug.Assert(asyncChildItem != null);
                                    if (asyncChildItem != null)
                                    {
                                        asyncChildItem.IsExpanded = true;
                                        asyncChildItem.UpdateLayout();
                                    }

                                    if (depth > 0)
                                    {
                                        await this.ExpandSubtree(child, depth - 1);
                                    }
                                }), System.Windows.Threading.DispatcherPriority.Background).Task;
                        }
                    }
                }
            }
        }
    }
}
