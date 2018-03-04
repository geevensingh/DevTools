namespace JsonViewer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using JsonViewer.View;
    using Utilities;

    public class JsonObject : NotifyPropertyChanged
    {
        private JsonObject _parent = null;
        private List<JsonObject> _children = new List<JsonObject>();
        private List<JsonObject> _allChildren = null;
        private TreeViewData _viewObject = null;
        private string _key;
        private object _value;
        private string _valueString;
        private string _originalString;
        private object _typedValue;
        private DataType _dataType = DataType.Other;
        private List<ConfigRule> _rules;
        private FindRule _findRule = null;

        public JsonObject(string key, object value, JsonObject parent)
            : this(key, value)
        {
            _parent = parent;
        }

        protected JsonObject(string key, object value)
        {
            _key = key;
            _originalString = value as string;
            this.Value = value;

            _rules = new List<ConfigRule>(Config.This.Rules.Where(rule => rule.Matches(this)));
        }

        public enum DataType
        {
            Json,
            Array,
            Guid,
            Other,
            ParsableString
        }

        public string Key { get => _key; }

        public object Value
        {
            get => _value;
            private set
            {
                _value = value;
                _typedValue = GetTypedValue(_value, out _dataType);

                if (_dataType == DataType.Array || _dataType == DataType.Json)
                {
                    System.Web.Script.Serialization.JavaScriptSerializer ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                    _valueString = ser.Serialize(_value);
                }
                else if (_value == null)
                {
                    _valueString = "null";
                }
                else if (_value is bool)
                {
                    _valueString = _value.ToString().ToLower();
                }
                else
                {
                    _valueString = _value.ToString();
                }

                Debug.Assert(string.IsNullOrEmpty(_value as string) || !string.IsNullOrEmpty(_valueString));
            }
        }

        public object TypedValue { get => _typedValue; }

        public JsonObject Parent { get => _parent; }

        public string Path
        {
            get
            {
                if (string.IsNullOrEmpty(_parent?.Path))
                {
                    return this.Key;
                }

                List<string> list = new List<string>();
                list.Add(_parent.Path);
                list.Add(this.Key);
                return string.Join("\\", list.ToArray());
            }
        }

        public virtual RootObject Root
        {
            get
            {
                if (_parent == null)
                {
                    Debug.Assert(this as RootObject != null);
                    return this as RootObject;
                }

                return _parent.Root;
            }
        }

        public int OverallIndex { get => this.Root.AllChildren.IndexOf(this); }

        public virtual IList<JsonObject> Children { get => _children; }

        public bool HasChildren { get => this.Children.Count > 0; }

        public DataType Type { get => _dataType; }

        public bool IsFindMatch { get => _findRule != null; }

        public bool CanTreatAsJson { get => _dataType == DataType.ParsableString; }

        public bool CanTreatAsText { get => _dataType == DataType.Json; }

        public string ValueString { get => _valueString; }

        public string PrettyValueString { get => this.GetPrettyString(); }

        public int TotalChildCount { get => this.Children.Count + this.Children.Sum(x => x.TotalChildCount); }

        public List<JsonObject> AllChildren
        {
            get
            {
                if (_allChildren == null)
                {
                    _allChildren = new List<JsonObject>();
                    foreach (JsonObject child in _children)
                    {
                        _allChildren.Add(child);
                        _allChildren.AddRange(child.AllChildren);
                    }
                }

                Debug.Assert(_allChildren.Count == this.TotalChildCount);
                return _allChildren;
            }
        }

        internal TreeViewData ViewObject
        {
            get
            {
                return _viewObject;
            }

            set
            {
                Debug.Assert(_viewObject == null);
                _viewObject = value;
            }
        }

        internal List<ConfigRule> Rules { get => _rules; }

        internal FindRule FindRule
        {
            get => _findRule;
            set
            {
                FindRule oldRule = _findRule;
                FindRule newRule = value;

                Debug.Assert(newRule == null || newRule.Matches(this));

                if (oldRule == null && newRule == null)
                {
                    return;
                }

                if (oldRule != null && newRule != null)
                {
                    // Even though the old rule and the new rule are different,
                    // let's treat them as the same and assume that they apply
                    // the same formatting rules.
                    return;
                }

                if (_findRule != null)
                {
                    _rules.Remove(_findRule);
                }

                _findRule = newRule;

                if (newRule != null)
                {
                    _rules.Add(newRule);
                }

                this.FirePropertyChanged("FindRule");
            }
        }

        public virtual void AddChildren(IList<JsonObject> children)
        {
            Debug.Assert(_children.Count == 0);
            _children = new List<JsonObject>(children);
            this.Value = this.Value;
            if (_children.Count > 1)
            {
                this.FireChildrenChanged();
            }
        }

        public void TreatAsJson()
        {
            Debug.Assert(this.CanTreatAsJson);

            Dictionary<string, object> dict = JsonObjectFactory.TryDeserialize(this.Value as string);
            Debug.Assert(dict != null);
            this.Value = dict;
            _dataType = DataType.Json;
            JsonObjectFactory.Flatten(ref _children, dict, this);
            this.FireChildrenChanged();

            _parent.UpdateChild(this);
        }

        public void TreatAsText()
        {
            Debug.Assert(this.CanTreatAsText);

            if (string.IsNullOrEmpty(_originalString))
            {
                System.Web.Script.Serialization.JavaScriptSerializer ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                this.Value = ser.Serialize(this.Value);
            }
            else
            {
                this.Value = _originalString;
            }

            _dataType = DataType.ParsableString;
            _children.Clear();
            this.AddChildren(_children);

            _parent.UpdateChild(this);
        }

        internal TreeViewData ResetView()
        {
            CustomTreeView tree = _viewObject.Tree;
            _viewObject = null;
            TreeViewDataFactory.CreateNode(tree, this);
            Debug.Assert(_viewObject != null);
            return _viewObject;
        }

        protected virtual void UpdateChild(JsonObject child)
        {
            Debug.Assert(_children.Contains(child));
            int index = _children.IndexOf(child);
            Debug.Assert(_children[index].ViewObject == _viewObject.Children[index]);
            _viewObject.Children.RemoveAt(index);
            _viewObject.Children.Insert(index, child.ResetView());
        }

        protected virtual void FireChildrenChanged()
        {
            _allChildren = null;
            _parent?.FireChildrenChanged();
            this.FirePropertyChanged(new string[] { "AllChildren", "Children", "HasChildren", "ValueString", "TotalChildCount" });
        }

        private static object GetTypedValue(object value, out DataType dataType)
        {
            dataType = DataType.Other;
            if (value == null)
            {
                return null;
            }

            Type valueType = value.GetType();
            if (valueType == typeof(System.Collections.ArrayList))
            {
                dataType = DataType.Array;
                return value;
            }

            if (valueType == typeof(Dictionary<string, object>))
            {
                dataType = DataType.Json;
                return value;
            }

            // If this isn't a string, then we can't make a better type
            if (value.GetType() != typeof(string))
            {
                return value;
            }

            string str = (string)value;
            if (Guid.TryParse(str, out Guid guidValue))
            {
                dataType = DataType.Guid;
                return guidValue;
            }

            if (double.TryParse(str, out double doubleValue))
            {
                return doubleValue;
            }

            if (TimeSpan.TryParse(str, out TimeSpan timeSpanValue))
            {
                return timeSpanValue;
            }

            if (DateTime.TryParse(str, out DateTime dateTimeValue))
            {
                return dateTimeValue;
            }

            try
            {
                Uri uri = new Uri(str);
                return uri;
            }
            catch (UriFormatException)
            {
            }

            Dictionary<string, object> jsonObj = JsonObjectFactory.TryDeserialize(str);
            if (jsonObj != null)
            {
                dataType = DataType.ParsableString;
                return jsonObj;
            }

            return str;
        }

        private string GetPrettyKeySting(int depth)
        {
            if (_parent == null || _parent._dataType == DataType.Array || depth == 0)
            {
                return string.Empty;
            }

            return "\"" + _key + "\": ";
        }

        private string GetWrapString(bool start)
        {
            switch (_dataType)
            {
                case DataType.Array:
                    return start ? "[" : "]";
                case DataType.Json:
                    return start ? "{" : "}";
                default:
                    Debug.Assert(!this.HasChildren);
                    return string.Empty;
            }
        }

        private string GetPrettyString(int depth = 0)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Utilities.StringHelper.GeneratePrefix(depth, "  "));
            sb.Append(this.GetPrettyKeySting(depth));

            if (this.HasChildren)
            {
                sb.Append(this.GetWrapString(start: true));
                sb.Append("\r\n");
                List<string> childStrings = new List<string>();
                foreach (JsonObject child in this.Children)
                {
                    childStrings.Add(child.GetPrettyString(depth + 1));
                }

                sb.Append(string.Join(",\r\n", childStrings.ToArray()));
                sb.Append("\r\n");
                sb.Append(Utilities.StringHelper.GeneratePrefix(depth, "  "));
                sb.Append(this.GetWrapString(start: false));
            }
            else
            {
                if (_originalString == null)
                {
                    sb.Append(this.ValueString);
                }
                else
                {
                    sb.Append("\"");
                    sb.Append(Utilities.CSEscape.Escape(_originalString));
                    sb.Append("\"");
                }
            }

            return sb.ToString();
        }
    }
}
