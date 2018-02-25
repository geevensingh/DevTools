namespace JsonViewer
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Web.Script.Serialization;
    using System.Windows.Media;
    using Utilities;

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
        private Dictionary<ConfigValue, Color> _colors = new Dictionary<ConfigValue, Color>();
        private Dictionary<ConfigValue, Brush> _brushes = new Dictionary<ConfigValue, Brush>();
        private IList<ConfigRule> _rules = null;

        private Config()
        {
            Debug.Assert(_this == null);
            _this = this;

            this.IsDefault = true;

            string filePath = string.Empty;
            if (File.Exists(Properties.Settings.Default.ConfigPath))
            {
                filePath = Properties.Settings.Default.ConfigPath;
            }
            else if (File.Exists("Config.json"))
            {
                filePath = "Config.json";
            }

            JavaScriptSerializer ser = new JavaScriptSerializer();
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    _rawValues = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(filePath));
                    this.IsDefault = false;
                }
                catch
                {
                }
            }

            if (_rawValues == null)
            {
                _rawValues = ser.Deserialize<Dictionary<string, object>>(Properties.Settings.Default.DefaultConfigJson);
            }

            if (_rawValues.ContainsKey("treeViewFontSize"))
            {
                _rawValues["treeViewFontSize"] = Converters.ToDouble(_rawValues["treeViewFontSize"]).Value;
            }

            _rules = ConfigRule.GenerateRules((ArrayList)_rawValues["treeViewHighlights"]);
        }

        public bool IsDefault { get; private set; }

        internal static Config This
        {
            get
            {
                if (_this == null)
                {
                    Config.Reload();
                }

                Debug.Assert(_this != null);
                return _this;
            }
        }

        internal IList<ConfigRule> Rules { get => _rules; }

        public static void Reload()
        {
            _this = null;
            _this = new Config();
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

        public Brush GetHightlightColor(JsonObject obj)
        {
            ConfigRule rule = obj.Rules.FirstOrDefault(x => x.ForegroundBrush != null);
            if (rule != null)
            {
                return rule.ForegroundBrush;
            }

            return this.GetBrush(ConfigValue.TreeViewForeground);
        }

        internal double GetHighlightFontSize(JsonObject obj)
        {
            ConfigRule rule = obj.Rules.FirstOrDefault(x => x.FontSize.HasValue);
            if (rule != null)
            {
                return rule.FontSize.Value;
            }

            if (_rawValues.ContainsKey("treeViewFontSize"))
            {
                return (double)_rawValues["treeViewFontSize"];
            }

            return 12.0;
        }
    }
}
