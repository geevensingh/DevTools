﻿namespace JsonViewer
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Web.Script.Serialization;
    using System.Windows.Media;

    internal enum ConfigValue
    {
        TreeViewForeground,
        TreeViewHighlightBrushKey,
        TreeViewHighlightTextBrushKey,
        TreeViewInactiveSelectionHighlightBrushKey,
        TreeViewInactiveSelectionHighlightTextBrushKey,
        TreeViewSearchResultForeground,
        TreeViewSearchResultBackground,
        TreeViewSelectedItemParent
    }

    internal class Config
    {
        private static Config _this = null;
        private Dictionary<string, object> _rawValues = null;
        private Dictionary<string, SolidColorBrush> _highlightColor = new Dictionary<string, SolidColorBrush>();
        private Dictionary<string, double> _highlightFontSize = new Dictionary<string, double>();
        private Dictionary<ConfigValue, Color> _colors = new Dictionary<ConfigValue, Color>();
        private Dictionary<ConfigValue, Brush> _brushes = new Dictionary<ConfigValue, Brush>();

        private Config()
        {
            Debug.Assert(_this == null);
            _this = this;

            JavaScriptSerializer ser = new JavaScriptSerializer();
            try
            {
                _rawValues = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(Properties.Settings.Default.ConfigPath));
            }
            catch
            {
            }

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

        internal static Config This
        {
            get
            {
                if (_this == null)
                {
                    _this = new Config();
                }

                return _this;
            }
        }

        public static Config Reload()
        {
            _this = null;
            return new Config();
        }

        public static bool SetPath(string fileName)
        {
            if (!File.Exists(fileName))
            {
                return false;
            }

            JavaScriptSerializer ser = new JavaScriptSerializer();
            try
            {
                ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(fileName));
            }
            catch
            {
                return false;
            }

            string oldConfigPath = Properties.Settings.Default.ConfigPath;
            try
            {
                Properties.Settings.Default.ConfigPath = fileName;
                Reload();
            }
            catch
            {
                Properties.Settings.Default.ConfigPath = oldConfigPath;
                return false;
            }

            Properties.Settings.Default.Save();
            return true;
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
            try
            {
                if (!_colors.ContainsKey(configValue))
                {
                    string key = configValue.ToString();
                    _colors[configValue] = (Color)ColorConverter.ConvertFromString(_rawValues[key] as string);
                }

                return _colors[configValue];
            }
            catch
            {
                switch (configValue)
                {
                    case ConfigValue.TreeViewForeground:
                        return Colors.DarkGray;
                    case ConfigValue.TreeViewHighlightBrushKey:
                        return Colors.Yellow;
                    case ConfigValue.TreeViewHighlightTextBrushKey:
                        return Colors.Black;
                    case ConfigValue.TreeViewInactiveSelectionHighlightBrushKey:
                        return Colors.LightYellow;
                    case ConfigValue.TreeViewInactiveSelectionHighlightTextBrushKey:
                        return Colors.Black;
                    case ConfigValue.TreeViewSearchResultForeground:
                        return Colors.Blue;
                    case ConfigValue.TreeViewSearchResultBackground:
                        return Colors.LightGreen;
                    case ConfigValue.TreeViewSelectedItemParent:
                        return Color.FromArgb(0x80, Colors.Aquamarine.R, Colors.Aquamarine.G, Colors.Aquamarine.B);
                    default:
                        Debug.Assert(false);
                        return Colors.Transparent;
                }
            }
        }

        public Brush GetHightlightColor(string key)
        {
            if (_highlightColor.ContainsKey(key))
            {
                return _highlightColor[key];
            }

            return this.GetBrush(ConfigValue.TreeViewForeground);
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