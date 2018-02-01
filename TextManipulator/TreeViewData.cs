using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows;
using System.Diagnostics;

namespace TextManipulator
{
    public class TreeViewData
    {
        private TreeViewData _parent;
        private string _key = string.Empty;
        private object _value = "Value";    // string.Empty;
        private string _oneLineValue = string.Empty;
        private List<TreeViewData> _children = new List<TreeViewData>();
        private bool _expanded = false;
        private bool? _expectChildren = null;

        public string KeyName { get => _key; }
        public string Value
        {
            get
            {
                if (this.ValueType == "object")
                {
                    System.Web.Script.Serialization.JavaScriptSerializer ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                    return ser.Serialize(_value);
                }
                return _value.ToString();
            }
        }
        public string OneLineValue { get => _oneLineValue; }
        public IList<TreeViewData> Children { get => _children; }
        public bool Expanded { get => _expanded; set => _expanded = value; }
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
                    parentPath += this._parent.KeyName;
                }

                return parentPath;
            }
        }
        public IList<TreeViewData> ParentList
        {
            get
            {
                List<TreeViewData> parentList = new List<TreeViewData>();
                if (this._parent != null)
                {
                    parentList.AddRange(this._parent.ParentList);
                    parentList.Add(this._parent);
                }
                return parentList;
            }
        }

        public TreeViewData Parent { get => _parent; }

        public TreeViewData(string key, object value, TreeViewData parent)
        {
            _key = key;
            _parent = parent;
            if (_parent != null)
            {
                _parent.AddChild(this);
            }

            SetValue(value);
            Debug.Assert(_expectChildren.HasValue);
        }

        private void SetValue(object value)
        {
            _value = value;
            if (value != null)
            {
                Type valueType = value.GetType();
                if (valueType == typeof(string))
                {
                    string str = value as string;
                    Guid guidValue;
                    if (Guid.TryParse(str, out guidValue))
                    {
                        _value = guidValue;
                    }
                    else
                    {
                        double doubleValue;
                        if (double.TryParse(str, out doubleValue))
                        {
                            _value = doubleValue;
                        }
                        else
                        {
                            DateTime dateTimeValue;
                            if (DateTime.TryParse(str, out dateTimeValue))
                            {
                                _value = dateTimeValue;
                            }
                            else
                            {
                                TimeSpan timeSpanValue;
                                if (TimeSpan.TryParse(str, out timeSpanValue))
                                {
                                    _value = timeSpanValue;
                                }
                                else
                                {
                                    System.Web.Script.Serialization.JavaScriptSerializer ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                                    try
                                    {
                                        Dictionary<string, object> jsonObj = ser.Deserialize<Dictionary<string, object>>(str);
                                        _value = jsonObj;
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                }
            }
            SetOneLineValue();
            Debug.Assert(_expectChildren.HasValue);
        }

        private void SetOneLineValue()
        {
            _oneLineValue = this.ValueType;
            if (_value != null)
            {
                if (!_expectChildren.Value)
                {
                    _oneLineValue = _value.ToString();
                }

                Type valueType = _value.GetType();
                if (valueType == typeof(DateTime))
                {
                    _oneLineValue += " (" + Utilities.TimeSpanStringify.PrettyApprox(DateTime.Now - (DateTime)_value) + ")";
                }
                else if (valueType == typeof(TimeSpan))
                {
                    _oneLineValue += " (" + Utilities.TimeSpanStringify.PrettyExact((TimeSpan)_value) + ")";
                }
            }
        }

        public Brush TextColor
        {
            get
            {
                switch (_key)
                {
                    case "events":
                        return Brushes.Green;
                    case "event_type":
                        return Brushes.Blue;
                    default:
                        return Brushes.Black;
                }
            }
        }

        public string ValueType
        {
            get
            {
                if (_value == null)
                {
                    _expectChildren = false;
                    return "null";
                }

                _expectChildren = true;
                Type valueType = _value.GetType();
                if (valueType == typeof(System.Collections.ArrayList))
                {
                    return "array[" + (_value as System.Collections.ArrayList).Count + "]";
                }

                if (valueType == typeof(Dictionary<string, object>))
                {
                    return "object";
                }

                _expectChildren = false;
                return valueType.ToString();
            }
        }

        public Visibility ShowSomething
        {
            get
            {
                return (_value.GetType() == typeof(Guid)) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public void AddChild(TreeViewData child)
        {
            Debug.Assert(_expectChildren.Value);
            _children.Add(child);
        }
    }
}
