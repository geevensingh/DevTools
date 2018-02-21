namespace JsonViewer
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Media;
    using JsonViewer.Commands.PerItem;

    internal class TreeViewData : NotifyPropertyChanged
    {
        private JsonObject _jsonObject;
        private string _oneLineValue = string.Empty;
        private ObservableCollection<TreeViewData> _children = new ObservableCollection<TreeViewData>();
        private bool _isSelected = false;
        private bool _isChildSelected = false;

        internal TreeViewData(JsonObject jsonObject, IList<TreeViewData> children)
        {
            _jsonObject = jsonObject;
            _jsonObject.ViewObject = this;
            _children = new ObservableCollection<TreeViewData>(children);

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

            _jsonObject.PropertyChanged += OnDataModelPropertyChanged;
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;

            this.ExpandChildrenCommand = new ExpandChildrenCommand(this);
            this.ExpandAllCommand = new ExpandAllCommand(this);
            this.CollapseAllCommand = new CollapseAllCommand(this);
            this.CopyKeyCommand = new CopyKeyCommand(this);
            this.CopyValueCommand = new CopyValueCommand(this);
            this.CopyEscapedValueCommand = new CopyEscapedValueCommand(this);
            this.TreatAsJsonCommand = new TreatAsJsonCommand(this);
            this.TreatAsTextCommand = new TreatAsTextCommand(this);
        }

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

        public TreeViewData Parent { get => _jsonObject.Parent?.ViewObject; }

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

        public Brush TextColor
        {
            get
            {
                if (this._isSelected)
                {
                    return Config.This.GetBrush(ConfigValue.TreeViewHighlightTextBrushKey);
                }

                if (_jsonObject.IsFindMatch)
                {
                    return Config.This.GetBrush(ConfigValue.TreeViewSearchResultForeground);
                }

                return Config.This.GetHightlightColor(_jsonObject.Key);
            }
        }

        public Brush BackgroundColor
        {
            get
            {
                if (this._isSelected)
                {
                    return Config.This.GetBrush(ConfigValue.TreeViewHighlightBrushKey);
                }

                if (_jsonObject.IsFindMatch)
                {
                    return Config.This.GetBrush(ConfigValue.TreeViewSearchResultBackground);
                }

                if (_isChildSelected && Properties.Settings.Default.HighlightSelectedParents)
                {
                    return Config.This.GetBrush(ConfigValue.TreeViewSelectedItemParent);
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

        public double FontSize
        {
            get
            {
                return Config.This.GetHighlightFontSize(_jsonObject.Key);
            }
        }

        public bool IsChildSelected
        {
            get
            {
                return _isChildSelected;
            }

            set
            {
                if (this.SetValue(ref _isChildSelected, value, new string[] { "BackgroundColor", "TextColor" }))
                {
                    if (this.Parent != null)
                    {
                        this.Parent.IsChildSelected = _isChildSelected;
                    }
                }
            }
        }

        public bool IsSelected
        {
            get
            {
                return _isSelected;
            }

            set
            {
                if (this.SetValue(ref _isSelected, value, new string[] { "BackgroundColor", "TextColor" }))
                {
                    if (this.Parent != null)
                    {
                        this.Parent.IsChildSelected = _isSelected;
                    }
                }
            }
        }

        public ExpandChildrenCommand ExpandChildrenCommand { get; private set; }

        public ExpandAllCommand ExpandAllCommand { get; private set; }

        public CollapseAllCommand CollapseAllCommand { get; private set; }

        public CopyKeyCommand CopyKeyCommand { get; private set; }

        public CopyValueCommand CopyValueCommand { get; private set; }

        public CopyEscapedValueCommand CopyEscapedValueCommand { get; private set; }

        public TreatAsJsonCommand TreatAsJsonCommand { get; private set; }

        public TreatAsTextCommand TreatAsTextCommand { get; private set; }

        internal JsonObject JsonObject { get => _jsonObject; }

        private void OnDataModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "IsFindMatch":
                    this.FirePropertyChanged("TextColor");
                    this.FirePropertyChanged("BackgroundColor");
                    break;
                case "HasChildren":
                    this.FirePropertyChanged(new string[] { "HasChildren", "ValueType" });
                    break;
                case "TotalChildCount":
                    this.FirePropertyChanged(new string[] { "ValueType", "OneLineValue" });
                    break;
                case "ValueString":
                    this.FirePropertyChanged("Value");
                    break;
                case "Children":
                case "AllChildren":
                    this.FirePropertyChanged("AllChildren");
                    break;
                default:
                    Debug.Assert(false, "Unknown property change");
                    break;
            }
        }

        private void OnSettingsPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "HighlightSelectedParents" && _isChildSelected)
            {
                this.FirePropertyChanged("BackgroundColor");
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
    }
}
