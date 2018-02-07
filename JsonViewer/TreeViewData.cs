using System;
using System.Collections.Generic;
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
        private List<TreeViewData> _children = new List<TreeViewData>();

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
        public IList<TreeViewData> Children { get => _children; }
        public TreeViewData Parent { get => (_jsonObject.Parent == null) ? null : _jsonObject.Parent.ViewObject; }

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
            foreach(JsonObject childData in _jsonObject.Children)
            {
                _children.Add(childData.ViewObject);
            }

            SetValue();

            _jsonObject.PropertyChanged += OnDataModelPropertyChanged;
        }

        private void OnDataModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsFindMatch")
            {
                this.FirePropertyChanged("TextColor");
                this.FirePropertyChanged("BackgroundColor");
            }
        }

        private void SetValue()
        {
            SetOneLineValue();
        }

        private void SetOneLineValue()
        {
            _oneLineValue = this.ValueType;
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
                object value = _jsonObject.Value;
                if (value == null)
                {
                    return "null";
                }

                switch (_jsonObject.Type)
                {
                    case JsonObject.DataType.Array:
                        return "array[" + (value as System.Collections.ArrayList).Count + "]";
                    case JsonObject.DataType.Json:
                        return "json-object";
                    default:
                        return Utilities.StringHelper.TrimStart(value.GetType().ToString(), "System.");
                }
            }
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
    }
}
