namespace JsonViewer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

    internal class JsonObject : NotifyPropertyChanged
    {
        private JsonObject _parent = null;
        private List<JsonObject> _children = new List<JsonObject>();
        private TreeViewData _viewObject = null;
        private string _key;
        private object _value;
        private string _originalString;
        private object _typedValue;
        private DataType _dataType = DataType.Other;
        private bool _isFindMatch = false;

        public JsonObject(string key, object value, JsonObject parent)
            : this(key, value)
        {
            _parent = parent;
            _parent.AddChild(this);
        }

        protected JsonObject(string key, object value)
        {
            _key = key;
            _value = value;
            _originalString = value as string;
            _typedValue = GetTypedValue(_value, out _dataType);
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

        public object RawValue { get => _value; }

        public object Value { get => _typedValue; }

        public JsonObject Parent { get => _parent; }

        public virtual IList<JsonObject> Children { get => _children; }

        public bool HasChildren { get => this.Children.Count > 0; }

        public DataType Type { get => _dataType; }

        public bool IsFindMatch { get => _isFindMatch; set => this.SetValue(ref _isFindMatch, value, "IsFindMatch"); }

        public bool CanTreatAsJson { get => _dataType == DataType.ParsableString; }

        public bool CanTreatAsText { get => _dataType == DataType.Json; }

        public string ParentPath
        {
            get
            {
                string parentPath = string.Empty;
                if (_parent != null)
                {
                    parentPath = _parent.ParentPath;
                    if (!string.IsNullOrEmpty(parentPath))
                    {
                        parentPath += " : ";
                    }

                    parentPath += _parent._key;
                }

                return parentPath;
            }
        }

        public string ValueString
        {
            get
            {
                if (this.Children.Count > 0)
                {
                    System.Web.Script.Serialization.JavaScriptSerializer ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                    return ser.Serialize(_value);
                }

                if (_value == null)
                {
                    return "null";
                }

                return _value.ToString();
            }
        }

        public int TotalChildCount
        {
            get
            {
                int result = this.Children.Count;
                foreach (JsonObject child in this.Children)
                {
                    result += child.TotalChildCount;
                }

                return result;
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

        public bool TreatAsJson()
        {
            if (!this.CanTreatAsJson)
            {
                return false;
            }

            Dictionary<string, object> dict = JsonObjectFactory.TryDeserialize(_value as string);
            Debug.Assert(dict != null);
            _value = dict;
            _dataType = DataType.Json;
            JsonObjectFactory.Flatten(ref _children, dict, this);

            _parent.RebuildViewObjects(this);

            return true;
        }

        public bool TreatAsText()
        {
            if (!this.CanTreatAsText)
            {
                return false;
            }

            if (string.IsNullOrEmpty(_originalString))
            {
                System.Web.Script.Serialization.JavaScriptSerializer ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                _value = ser.Serialize(_value);
            }
            else
            {
                _value = _originalString;
            }

            _dataType = DataType.ParsableString;
            _children.Clear();

            _parent.RebuildViewObjects(this);

            return true;
        }

        internal TreeViewData ResetView()
        {
            _viewObject = null;
            TreeViewDataFactory.CreateNode(this);
            Debug.Assert(_viewObject != null);
            return _viewObject;
        }

        protected virtual void AddChild(JsonObject child)
        {
            Debug.Assert(!this.Children.Contains(child));
            this.Children.Add(child);
        }

        protected virtual void RebuildViewObjects(JsonObject child)
        {
            Debug.Assert(_children.Contains(child));
            int index = _children.IndexOf(child);
            Debug.Assert(_children[index].ViewObject == _viewObject.Children[index]);
            _viewObject.Children.RemoveAt(index);
            _viewObject.Children.Insert(index, child.ResetView());
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
            Guid guidValue;
            if (Guid.TryParse(str, out guidValue))
            {
                dataType = DataType.Guid;
                return guidValue;
            }

            double doubleValue;
            if (double.TryParse(str, out doubleValue))
            {
                return doubleValue;
            }

            DateTime dateTimeValue;
            if (DateTime.TryParse(str, out dateTimeValue))
            {
                return dateTimeValue;
            }

            TimeSpan timeSpanValue;
            if (TimeSpan.TryParse(str, out timeSpanValue))
            {
                return timeSpanValue;
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
    }
}
