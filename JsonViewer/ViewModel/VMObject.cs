namespace JsonViewer.ViewModel
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Windows;
    using JsonViewer.Model;
    using JsonViewer.View;
    using Utilities;

    public class VMObject : NotifyPropertyChanged
    {
        private JsonObject _jsonObject = null;
        private bool _isExpanded = true;
        private List<VMObject> _children = null;

        public VMObject(JsonObject jsonObject)
        {
            _jsonObject = jsonObject;
            _jsonObject.PropertyChanged += OnJsonObjectPropertyChanged;
        }

        public JsonObject Data { get => _jsonObject; }

        public Visibility ExpandButtonVisibility
        {
            get
            {
                return this.HasChildren ? Visibility.Visible : Visibility.Hidden;
            }
        }

        public string ExpandButtonContent
        {
            get
            {
                return this.IsExpanded ? "-" : "+";
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded && this.HasChildren;
            set
            {
                this.SetValue(ref _isExpanded, value, new string[] { "IsExpanded", "IsCollapsed", "ExpandButtonContent" });
            }
        }

        public bool IsCollapsed
        {
            get => !_isExpanded && this.HasChildren;
            set
            {
                this.IsExpanded = !value;
            }
        }

        public void ToggleExpand()
        {
            this.IsExpanded = !this.IsExpanded;
        }

        public bool HasChildren
        {
            get => _jsonObject.HasChildren;
        }

        public IList<TreeViewData> GetVisibleList()
        {
            List<TreeViewData> flatList = new List<TreeViewData>();
            foreach (VMObject child in this.GetVisibleChildren())
            {
                flatList.Add(child._jsonObject.ViewObject);
                flatList.AddRange(child.GetVisibleList());
            }

            return flatList;
        }

        public IList<VMObject> GetVisibleChildren()
        {
            if (this.IsExpanded)
            {
                return this.GetChildren();
            }

            return new List<VMObject>();
        }

        private IList<VMObject> GetChildren()
        {
            if (_children == null)
            {
                _children = new List<VMObject>();
                foreach (JsonObject dataChildren in _jsonObject.Children)
                {
                    _children.Add(new VMObject(dataChildren));
                }
            }

            return _children;
        }

        private void OnJsonObjectPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Debug.Assert(sender == _jsonObject);
            switch (e.PropertyName)
            {
                case "Children":
                    _children = null;

                    break;
                default:
                    break;
            }
        }
    }
}
