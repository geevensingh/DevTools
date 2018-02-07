using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace JsonViewer
{
    internal class JsonObject : NotifyPropertyChanged
    {
        public enum DataType
        {
            Json,
            Array,
            Guid,
            Other
        }

        private JsonObject _parent = null;
        private List<JsonObject> _children = new List<JsonObject>();
        private TreeViewData _viewObject = null;
        private string _key;
        private object _value;
        private object _typedValue;
        private DataType _dataType = DataType.Other;
        private bool _isFindMatch = false;

        public string Key { get => _key; }
        public object RawValue { get => _value; }
        public object Value { get => _typedValue; }
        internal JsonObject Parent { get => _parent; }
        internal virtual IList<JsonObject> Children { get => _children; }
        internal bool HasChildren { get => this.Children.Count > 0; }
        public DataType Type { get => _dataType; }
        public bool IsFindMatch { get => _isFindMatch; set => this.SetValue(ref _isFindMatch, value, "IsFindMatch"); }


        public JsonObject(string key, object value, JsonObject parent)
        {
            _key = key;
            _value = value;
            _parent = parent;
            if (_parent != null)
            {
                _parent.AddChild(this);
            }

            _typedValue = GetTypedValue(_value, out _dataType);
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

        public string ParentPath
        {
            get
            {
                string parentPath = string.Empty;
                if (this._parent != null)
                {
                    parentPath = this._parent.ParentPath;
                    if (!string.IsNullOrEmpty(parentPath))
                    {
                        parentPath += " : ";
                    }
                    parentPath += this._parent._key;
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
                int result = _children.Count;
                foreach (JsonObject child in _children)
                {
                    result += child.TotalChildCount;
                }
                return result;
            }
        }

        private void AddChild(JsonObject child)
        {
            Debug.Assert(!_children.Contains(child));
            _children.Add(child);
        }

        static private object GetTypedValue(object value, out DataType dataType)
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
            catch (UriFormatException) { }


            Dictionary<string, object> jsonObj = JsonObjectFactory.TryDeserialize(str);
            if (jsonObj != null)
            {
                dataType = DataType.Json;
                return jsonObj;
            }

            return str;
        }
    }
}
