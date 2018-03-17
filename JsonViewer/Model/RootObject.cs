namespace JsonViewer.Model
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
            Debug.Assert(_viewChildren == null);
            _viewChildren = null;
            base.SetChildren(children);
        }

        internal void SetTreeItemsSource(CustomTreeView tree)
        {
            if (_viewChildren == null)
            {
                _viewChildren = TreeViewDataFactory.CreateCollection(tree, this);
            }

            Debug.Assert(_viewChildren != null);
            Debug.Assert(_viewChildren.Count == 0 || _viewChildren[0].Tree == tree);
            tree.ItemsSource = _viewChildren;

            if (_expandByRules == null)
            {
                _expandByRules = new SingularAction(tree.Dispatcher);
            }

            Debug.Assert(_expandByRules.Dispatcher == tree.Dispatcher);

            _expandByRules.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                async (actionId, action) =>
                {
                    foreach (JsonObject jsonObj in this.AllChildren)
                    {
                        int? depth = jsonObj.Rules.Max(x => x.ExpandChildren);
                        if (depth.HasValue)
                        {
                            tree.ExpandToItem(jsonObj.ViewObject);
                            tree.ExpandSubtree(jsonObj.ViewObject, depth.Value);
                        }
                        if (!await action.YieldAndContinue(actionId))
                        {
                            return false;
                        }
                    }

                    return true;
                });
        }

        protected override void UpdateChild(JsonObject child)
        {
            Debug.Assert(this.Children.Contains(child));
            int index = this.Children.IndexOf(child);
            Debug.Assert(this.Children[index].ViewObject == _viewChildren[index]);
            _viewChildren.RemoveAt(index);
            _viewChildren.Insert(index, child.ResetView());
        }
    }
}
