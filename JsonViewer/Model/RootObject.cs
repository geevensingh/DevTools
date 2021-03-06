﻿namespace JsonViewer.Model
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using JsonViewer.View;
    using Utilities;

    public class RootObject : JsonObject
    {
        private static SingularAction _expandByRules = null;
        private ObservableCollection<TreeViewData> _viewChildren = null;

        public RootObject()
            : base(string.Empty, new Dictionary<string, object>())
        {
        }

        public static async Task<RootObject> Create(Dictionary<string, object> jsonObj)
        {
            if (jsonObj == null)
            {
                return null;
            }

            using (new WaitCursor())
            {
                return await Task.Run(
                    () =>
                    {
                        RootObject root = new RootObject();
                        var jsonObjects = new List<JsonObject>();
                        JsonObjectFactory.Flatten(ref jsonObjects, jsonObj, root);
                        return root;
                    });
            }
        }

        public override void SetChildren(IList<JsonObject> children)
        {
            FileLogger.Assert(_viewChildren == null);
            _viewChildren = null;
            base.SetChildren(children);
        }

        internal void SetTreeItemsSource(CustomTreeView tree)
        {
            if (_viewChildren == null)
            {
                _viewChildren = TreeViewDataFactory.CreateCollection(tree, this);
            }

            FileLogger.Assert(_viewChildren != null);
            FileLogger.Assert(_viewChildren.Count == 0 || _viewChildren[0].Tree == tree);
            tree.ItemsSource = _viewChildren;

            this.ApplyExpandRule(tree);
        }

        internal void ApplyExpandRule(CustomTreeView tree)
        {
            if (_expandByRules == null)
            {
                _expandByRules = new SingularAction(tree.Dispatcher);
            }

            FileLogger.Assert(_expandByRules.Dispatcher == tree.Dispatcher);

            bool expandedSomething = false;
            _expandByRules.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                async (actionId, action) =>
                {
                    foreach (JsonObject jsonObj in this.AllChildren)
                    {
                        int? depth = jsonObj.Rules.ExpandChildren;
                        if (depth.HasValue)
                        {
                            tree.ExpandToItem(jsonObj.ViewObject);
                            tree.ExpandSubtree(jsonObj.ViewObject, depth.Value);
                            expandedSomething = true;
                        }
                        if (!await action.YieldAndContinue(actionId))
                        {
                            return false;
                        }
                    }

                    if (!expandedSomething)
                    {
                        tree.ExpandAll(this.ExpandLevelWithLessThanCount(50));
                    }

                    return true;
                });
        }

        protected override void UpdateChild(JsonObject child)
        {
            FileLogger.Assert(this.Children.Contains(child));
            int index = this.Children.IndexOf(child);
            FileLogger.Assert(this.Children[index].ViewObject == _viewChildren[index]);
            _viewChildren.RemoveAt(index);
            _viewChildren.Insert(index, child.ResetView());
        }

        protected override void ApplyRules()
        {
            // Do nothing
        }

        private int ExpandLevelWithLessThanCount(int count)
        {
            int totalChildCount = this.TotalChildCount;
            int depth = 0;
            int lastCount = this.CountAtDepth(depth++);
            while (depth < count && lastCount < count && lastCount < totalChildCount)
            {
                lastCount += this.CountAtDepth(depth++);
            }

            if (lastCount > count)
            {
                return System.Math.Max(depth - 3, 0);
            }

            FileLogger.Assert(depth >= 2);
            return depth - 2;
        }
    }
}
