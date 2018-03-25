namespace JsonViewer.View
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
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
            return GetItemContainerGenerator(data.Parent)?.ContainerFromItem(data) as TreeViewItem;
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            ConfigValues configValues = Config.Values;
            this.Foreground = configValues.GetBrush(ConfigValue.DefaultForeground);
            this.Background = configValues.GetBrush(ConfigValue.DefaultBackground);
            this.Resources[SystemColors.HighlightBrushKey] = configValues.GetBrush(ConfigValue.SelectedBackground);
            this.Resources[SystemColors.HighlightTextBrushKey] = configValues.GetBrush(ConfigValue.SelectedForeground);
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
