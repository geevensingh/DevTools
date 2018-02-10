using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows;
using System.Diagnostics;

namespace JsonViewer
{
    internal class TreeViewData : NotifyPropertyChanged
    {
        private JsonObject _jsonObject;
        //private TreeViewData _parent;
        private string _oneLineValue = string.Empty;
        private ObservableCollection<TreeViewData> _children = new ObservableCollection<TreeViewData>();

        public string KeyName { get => _jsonObject.Key; }
        public string Value
        {
            get
            {
                if (_jsonObject.Value.GetType() == typeof(Guid))
                {
                    return _jsonObject.RawValue.ToString();
                }
                return _jsonObject.ValueString;
            }
        }
        public string OneLineValue { get => _oneLineValue; }
        public ObservableCollection<TreeViewData> Children { get => _children; }
        public TreeViewData Parent { get => (_jsonObject.Parent == null) ? null : _jsonObject.Parent.ViewObject; }
        public bool HasChildren { get => _jsonObject.HasChildren; }

        public IList<TreeViewData> ParentList
        {
            get
            {
                List<TreeViewData> parentList = new List<TreeViewData>();
                if (this.Parent != null)
                {
                    parentList.AddRange(this.Parent.ParentList);
                    parentList.Add(this.Parent);
                }
                return parentList;
            }
        }

        //public TreeViewData Parent { get => _parent; }

        internal TreeViewData(JsonObject jsonObject, IList<TreeViewData> children)
        {
            _jsonObject = jsonObject;
            _jsonObject.ViewObject = this;
            foreach (JsonObject childData in _jsonObject.Children)
            {
                _children.Add(childData.ViewObject);
            }

            SetValue();

            _jsonObject.PropertyChanged += OnDataModelPropertyChanged;
        }

        private void OnDataModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "IsFindMatch":
                    this.FirePropertyChanged("TextColor");
                    this.FirePropertyChanged("BackgroundColor");
                    break;
                default:
                    Debug.Assert(false, "Unknown property change");
                    break;
            }
        }

        private void SetValue()
        {
            SetOneLineValue();
        }

        private void SetOneLineValue()
        {
            _oneLineValue = this.GetValueTypeString(includeChildCount: false);
            object value = _jsonObject.Value;
            if (value != null)
            {
                if (!_jsonObject.HasChildren)
                {
                    _oneLineValue = _jsonObject.ValueString;
                }

                Type valueType = value.GetType();
                if (valueType == typeof(DateTime))
                {
                    _oneLineValue += " (" + Utilities.TimeSpanStringify.PrettyApprox(DateTime.Now - (DateTime)value) + ")";
                }
                else if (valueType == typeof(TimeSpan))
                {
                    _oneLineValue += " (" + Utilities.TimeSpanStringify.PrettyExact((TimeSpan)value) + ")";
                }
            }
        }

        public Brush TextColor
        {
            get
            {
                if (_jsonObject.IsFindMatch)
                {
                    return Config.This.GetBrush(ConfigValue.treeViewSearchResultForeground);
                }
                return Config.This.GetHightlightColor(_jsonObject.Key);
            }
        }

        public Brush BackgroundColor
        {
            get
            {
                if (_jsonObject.IsFindMatch)
                {
                    return Config.This.GetBrush(ConfigValue.treeViewSearchResultBackground);
                }
                return Brushes.Transparent;
            }
        }

        public string ValueType
        {
            get
            {
                return this.GetValueTypeString(includeChildCount: true);
            }
        }

        private string GetValueTypeString(bool includeChildCount)
        {
            object value = _jsonObject.Value;
            if (value == null)
            {
                return "null";
            }

            string type;
            switch (_jsonObject.Type)
            {
                case JsonObject.DataType.Array:
                    type = "array[" + (value as System.Collections.ArrayList).Count + "]";
                    break;
                case JsonObject.DataType.Json:
                    type = "json-object[" + (value as Dictionary<string, object>).Keys.Count + "]";
                    break;
                case JsonObject.DataType.ParsableString:
                    type = "parse-able-string";
                    break;
                default:
                    type = Utilities.StringHelper.TrimStart(value.GetType().ToString(), "System.");
                    break;
            }
            Debug.Assert(!string.IsNullOrEmpty(type));

            if (includeChildCount && this.HasChildren)
            {
                int childCount = _jsonObject.Children.Count;
                int totalChildCount = _jsonObject.TotalChildCount;
                if (childCount != totalChildCount)
                {
                    type += " (tree: " + totalChildCount + ")";
                }
            }
            return type;
        }

        internal void RemoveChildren()
        {
            _children.Clear();
        }

        internal void ReplaceChild(TreeViewData oldChild, TreeViewData newChild)
        {
            Debug.Assert(_children.Contains(oldChild));
            int index = _children.IndexOf(oldChild);
            Debug.Assert(index >= 0 && index < _children.Count);
            _children.RemoveAt(index);
            _children.Insert(index, newChild);
        }

        public double FontSize
        {
            get
            {
                return Config.This.GetHighlightFontSize(_jsonObject.Key);
            }
        }

        public Visibility ShowSomething
        {
            get
            {
                return (_jsonObject.Type == JsonObject.DataType.Guid) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public bool CanExpand { get => this.HasChildren; }
        public bool CanCollapse { get => this.HasChildren; }
        public bool CanExpandChildren
        {
            get
            {
                foreach(TreeViewData child in _children)
                {
                    if (child.CanExpand)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public Visibility ShowTreatAsJson { get => _jsonObject.CanTreatAsJson ? Visibility.Visible : Visibility.Collapsed; }
        public bool TreatAsJson() { return _jsonObject.TreatAsJson(); }
        public Visibility ShowTreatAsText { get => _jsonObject.CanTreatAsText ? Visibility.Visible : Visibility.Collapsed; }
        public bool TreatAsText() { return _jsonObject.TreatAsText(); }
    }
}
