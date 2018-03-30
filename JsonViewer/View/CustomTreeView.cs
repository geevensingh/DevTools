namespace JsonViewer.View
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System.Windows.Media;
    using JsonViewer.Model;
    using Utilities;

    internal class CustomTreeView : TreeView, INotifyPropertyChanged
    {
        private TreeViewData _selected = null;
        private int? _selectedIndex = null;
        private SingularAction _action;

        public CustomTreeView()
        {
            System.Diagnostics.Debug.Assert(System.Threading.Thread.CurrentThread.ManagedThreadId == 1);

            Config.PropertyChanged += OnConfigPropertyChanged;

            _action = new SingularAction(this.Dispatcher);
            _action.PropertyChanged += OnActionPropertyChanged;
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

        public bool IsWaiting { get => _action.IsRunning; }

        public void ExpandAll()
        {
            this.ExpandAll(int.MaxValue);
        }

        public void ExpandAll(int depth)
        {
            Func<Guid, SingularAction, Task<bool>> func = new Func<Guid, SingularAction, Task<bool>>(async (actionId, action) =>
            {
                using (new WaitCursor())
                {
                    foreach (TreeViewData child in this.Items)
                    {
                        if (!await this.ExpandSubtree(child, depth, actionId, action) || !await action.YieldAndContinue(actionId))
                        {
                            return false;
                        }
                    }
                }

                return true;
            });
            _action.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, func);
        }

        public void ExpandSubtree(TreeViewData data, int depth)
        {
            Func<Guid, SingularAction, Task<bool>> func = new Func<Guid, SingularAction, Task<bool>>(async (actionId, action) =>
            {
                bool result = false;
                using (new WaitCursor())
                {
                    result = await this.ExpandSubtree(data, depth, actionId, action);
                }

                return result;
            });
            _action.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, func);
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
            IList<TreeViewData.ParentData> parentDatas = treeViewData.GetParents();
            ItemsControl lastParent = this;
            foreach (TreeViewData.ParentData parentData in parentDatas)
            {
                lastParent = GetTreeViewItem(lastParent, parentData.treeViewData, parentData.childIndex);
            }

            return GetTreeViewItem(lastParent, treeViewData, treeViewData.Index);
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

        private TreeViewItem GetTreeViewItem(ItemsControl container, TreeViewData item, int childIndex)
        {
            if (container == null)
            {
                Debug.Assert(false);
                return null;
            }

            if (container.DataContext == item)
            {
                return container as TreeViewItem;
            }

            // Expand the current container
            if (container is TreeViewItem treeViewItem && !treeViewItem.IsExpanded)
            {
                treeViewItem.IsExpanded = true;
            }

            // Try to generate the ItemsPresenter and the ItemsPanel.
            // by calling ApplyTemplate.  Note that in the
            // virtualizing case even if the item is marked
            // expanded we still need to do this step in order to
            // regenerate the visuals because they may have been virtualized away.
            container.ApplyTemplate();
            ItemsPresenter itemsPresenter = (ItemsPresenter)container.Template.FindName("ItemsHost", container);
            if (itemsPresenter != null)
            {
                itemsPresenter.ApplyTemplate();
            }
            else
            {
                // The Tree template has not named the ItemsPresenter, 
                // so walk the descendants and find the child.
                itemsPresenter = FindVisualChild<ItemsPresenter>(container);
                if (itemsPresenter == null)
                {
                    container.UpdateLayout();

                    itemsPresenter = FindVisualChild<ItemsPresenter>(container);
                }
            }

            Panel itemsHostPanel = (Panel)VisualTreeHelper.GetChild(itemsPresenter, 0);

            // Ensure that the generator for this panel has been created.
            UIElementCollection children = itemsHostPanel.Children;

            CustomVirtualizingStackPanel virtualizingPanel = itemsHostPanel as CustomVirtualizingStackPanel;

            if (childIndex != -1)
            {
                TreeViewItem subContainer;
                if (virtualizingPanel != null)
                {
                    // Bring the item into view so 
                    // that the container will be generated.
                    virtualizingPanel.BringIntoView(childIndex);

                    subContainer = (TreeViewItem)container.ItemContainerGenerator.ContainerFromIndex(childIndex);
                }
                else
                {
                    subContainer = (TreeViewItem)container.ItemContainerGenerator.ContainerFromIndex(childIndex);

                    // Bring the item into view to maintain the 
                    // same behavior as with a virtualizing panel.
                    subContainer.BringIntoView();
                }

                if (subContainer != null && subContainer.DataContext == item)
                {
                    return subContainer as TreeViewItem;
                }
            }

            for (int i = 0, count = container.Items.Count; i < count; i++)
            {
                TreeViewItem subContainer;
                if (virtualizingPanel != null)
                {
                    // Bring the item into view so 
                    // that the container will be generated.
                    virtualizingPanel.BringIntoView(i);

                    subContainer = (TreeViewItem)container.ItemContainerGenerator.ContainerFromIndex(i);
                }
                else
                {
                    subContainer = (TreeViewItem)container.ItemContainerGenerator.ContainerFromIndex(i);

                    // Bring the item into view to maintain the 
                    // same behavior as with a virtualizing panel.
                    subContainer.BringIntoView();
                }

                if (subContainer != null)
                {
                    // Search the next level for the object.
                    TreeViewItem resultContainer = GetTreeViewItem(subContainer, item, -1);
                    if (resultContainer != null)
                    {
                        return resultContainer;
                    }
                    else
                    {
                        // The object is not under this TreeViewItem
                        // so collapse it.
                        subContainer.IsExpanded = false;
                    }
                }
            }

            return null;
        }

        private T FindVisualChild<T>(Visual visual)
            where T : Visual
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            {
                Visual child = (Visual)VisualTreeHelper.GetChild(visual, i);
                if (child != null)
                {
                    if (child is T correctlyTyped)
                    {
                        return correctlyTyped;
                    }

                    T descendent = FindVisualChild<T>(child);
                    if (descendent != null)
                    {
                        return descendent;
                    }
                }
            }

            return null;
        }

        public TreeViewItem GetItem(TreeViewData data)
        {
            return GetItemContainerGenerator(data.Parent)?.ContainerFromItem(data) as TreeViewItem;
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            this.SetColors();
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

        private void OnConfigPropertyChanged(string propertyName)
        {
            switch (propertyName)
            {
                case "Values":
                    this.SetColors();
                    break;
                default:
                    break;
            }
        }

        private void SetColors()
        {
            ConfigValues configValues = Config.Values;
            this.Foreground = configValues.GetBrush(ConfigValue.DefaultForeground);
            this.Background = configValues.GetBrush(ConfigValue.DefaultBackground);
            this.Resources[SystemColors.HighlightBrushKey] = configValues.GetBrush(ConfigValue.SelectedBackground);
            this.Resources[SystemColors.HighlightTextBrushKey] = configValues.GetBrush(ConfigValue.SelectedForeground);
        }

        private async Task<bool> ExpandSubtree(TreeViewData data, int depth, Guid actionId, SingularAction action)
        {
            if (!data.HasChildren)
            {
                return true;
            }

            if (depth <= 0)
            {
                this.CollapseSubtree(data);
                return true;
            }

            TreeViewItem item = GetItem(data);
            if (item == null)
            {
                return true;
            }

            item.IsExpanded = true;
            if (depth > 0)
            {
                item.UpdateLayout();

                if (this.SelectedItem != null)
                {
                    item = this.GetItem((TreeViewData)this.SelectedItem);
                    Debug.Assert(!double.IsNaN(item.ActualWidth));
                    Debug.Assert(!double.IsNaN(item.ActualHeight));
                    item.BringIntoView(new Rect(0, -50, item.ActualWidth, 100 + item.ActualHeight));
                }

                foreach (TreeViewData child in data.Children)
                {
                    await this.ExpandSubtree(child, depth - 1, actionId, action);
                    if (!await action.YieldAndContinue(actionId))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void OnActionPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsRunning")
            {
                NotifyPropertyChanged.FirePropertyChanged("IsWaiting", this, this.PropertyChanged);
            }
        }

        private void OnCopyCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            Debug.Assert(sender == this);
            bool? canExecute = (this.SelectedItem as TreeViewData)?.CopyValueCommand?.CanExecute(null);
            e.CanExecute = canExecute.HasValue && canExecute.Value;
        }

        private void OnCopyCommandExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            Debug.Assert(sender == this);
            (this.SelectedItem as TreeViewData)?.CopyValueCommand?.Execute(null);
        }

        private ItemContainerGenerator GetItemContainerGenerator(TreeViewData data)
        {
            if (data == null)
            {
                return this.ItemContainerGenerator;
            }

            ItemContainerGenerator generator = GetItemContainerGenerator(data.Parent);
            TreeViewItem treeViewItem = generator.ContainerFromItem(data) as TreeViewItem;
            return treeViewItem?.ItemContainerGenerator;
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
    }
}
