using System;
using System.Web.Script.Serialization;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Diagnostics;

namespace JsonViewer
{
    enum ConfigValue
    {
        treeViewForeground,
        treeViewHighlightBrushKey,
        treeViewHighlightTextBrushKey,
        treeViewInactiveSelectionHighlightBrushKey,
        treeViewInactiveSelectionHighlightTextBrushKey,
        treeViewHighlights,
        treeViewSearchResultForeground,
        treeViewSearchResultBackground
    }
    class Config
    {
        private static Config _this = null;
        internal static Config This { get { if (_this == null) { _this = new Config(); } return _this; } }

        Dictionary<string, object> _rawValues = null;
        Dictionary<string, SolidColorBrush> _highlightColor = new Dictionary<string, SolidColorBrush>();
        Dictionary<string, double> _highlightFontSize = new Dictionary<string, double>();
        Dictionary<ConfigValue, Color> _colors = new Dictionary<ConfigValue, Color>();
        Dictionary<ConfigValue, Brush> _brushes = new Dictionary<ConfigValue, Brush>();
        private Config()
        {
            Debug.Assert(_this == null);
            _this = this;

            JavaScriptSerializer ser = new JavaScriptSerializer();
            try
            {
                _rawValues = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(Properties.Settings.Default.ConfigPath));
            }
            catch { }

            if (_rawValues == null)
            {
                _rawValues = ser.Deserialize<Dictionary<string, object>>(Properties.Settings.Default.ConfigJson);
            }

            if (_rawValues.ContainsKey("treeViewFontSize"))
            {
                _rawValues["treeViewFontSize"] = ConvertToDouble(_rawValues["treeViewFontSize"]);
            }

            ArrayList highlightsList = (ArrayList)_rawValues["treeViewHighlights"];
            foreach (Dictionary<string, object> highlightObj in highlightsList)
            {
                string key = highlightObj["keyName"] as string;
                Debug.Assert(!string.IsNullOrEmpty(key));

                string color = highlightObj["color"] as string;
                if (!string.IsNullOrEmpty(color))
                {
                    _highlightColor[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                }

                if (highlightObj.ContainsKey("fontSize"))
                {
                    _highlightFontSize[key] = ConvertToDouble(highlightObj["fontSize"]);
                }
            }
        }

        public static Config Reload()
        {
            _this = null;
            return new Config();
        }

        public Brush GetBrush(ConfigValue configValue)
        {
            if (!_brushes.ContainsKey(configValue))
            {
                _brushes[configValue] = new SolidColorBrush(this.GetColor(configValue));
            }
            return _brushes[configValue];
        }

        public Color GetColor(ConfigValue configValue)
        {
            if (!_colors.ContainsKey(configValue))
            {
                string key = configValue.ToString();
                _colors[configValue] = (Color)ColorConverter.ConvertFromString(_rawValues[key] as string);
            }
            return _colors[configValue];
        }

        public Brush GetHightlightColor(string key)
        {
            if (_highlightColor.ContainsKey(key))
            {
                return _highlightColor[key];
            }
            return this.GetBrush(ConfigValue.treeViewForeground);
        }

        internal double GetHighlightFontSize(string key)
        {
            if (_highlightFontSize.ContainsKey(key))
            {
                return _highlightFontSize[key];
            }
            if (_rawValues.ContainsKey("treeViewFontSize"))
            {
                return (double)_rawValues["treeViewFontSize"];
            }
            return 12.0;
        }

        private static double ConvertToDouble(object obj)
        {
            if (obj.GetType() == typeof(int))
            {
                return (int)obj;
            }

            if (obj.GetType() == typeof(float))
            {
                return (float)obj;
            }

            if (obj.GetType() == typeof(double))
            {
                return (double)obj;
            }

            Debug.Fail("invalid double: " + obj.ToString());
            return double.MinValue;
        }
    }
}
