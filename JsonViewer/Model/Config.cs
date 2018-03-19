namespace JsonViewer.Model
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Web.Script.Serialization;
    using System.Windows;
    using System.Windows.Media;
    using Microsoft.Win32;
    using Newtonsoft.Json;
    using Utilities;

    internal enum ConfigValue
    {
        DefaultForeground,
        DefaultBackground,
        SelectedForeground,
        SelectedBackground,
        SearchResultForeground,
        SearchResultBackground,
        SimilarNodeForeground,
        SimilarNodeBackground,
        SelectedParentForeground,
        SelectedParentBackground,
    }

    [JsonObject]
    internal class Config : NotifyPropertyChanged
    {
        private static Config _this = null;
        private static string _filePath = null;

        private Dictionary<string, object> _rawValues = new Dictionary<string, object>();
        private Dictionary<ConfigValue, Color> _colors = new Dictionary<ConfigValue, Color>();
        private Dictionary<ConfigValue, Brush> _brushes = new Dictionary<ConfigValue, Brush>();
        private IList<ConfigRule> _rules = new List<ConfigRule>();

        private Config()
        {
            Debug.Assert(_this == null);
            _this = this;
            this.IsDefault = true;
        }

        [JsonIgnore]
        public static Config This
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

        [JsonIgnore]
        public bool IsDefault { get; private set; }

        public string DefaultForeground { get => this.GetRawValue("DefaultForeground", "Black"); set => this.SetRawValue("DefaultForeground", value); }

        public string DefaultBackground { get => this.GetRawValue("DefaultBackground", "Transparent"); set => this.SetRawValue("DefaultBackground", value); }

        public string SelectedForeground { get => this.GetRawValue("SelectedForeground", this.DefaultForeground); set => this.SetRawValue("SelectedForeground", value); }

        public string SelectedBackground { get => this.GetRawValue("SelectedBackground", this.DefaultBackground); set => this.SetRawValue("SelectedBackground", value); }

        public string SearchResultForeground { get => this.GetRawValue("SearchResultForeground", this.DefaultForeground); set => this.SetRawValue("SearchResultForeground", value); }

        public string SearchResultBackground { get => this.GetRawValue("SearchResultBackground", this.DefaultBackground); set => this.SetRawValue("SearchResultBackground", value); }

        public string SimilarNodeForeground { get => this.GetRawValue("SimilarNodeForeground", this.DefaultForeground); set => this.SetRawValue("SimilarNodeForeground", value); }

        public string SimilarNodeBackground { get => this.GetRawValue("SimilarNodeBackground", this.DefaultBackground); set => this.SetRawValue("SimilarNodeBackground", value); }

        public string SelectedParentForeground { get => this.GetRawValue("SelectedParentForeground", this.DefaultForeground); set => this.SetRawValue("SelectedParentForeground", value); }

        public string SelectedParentBackground { get => this.GetRawValue("SelectedParentBackground", this.DefaultBackground); set => this.SetRawValue("SelectedParentBackground", value); }

        public double DefaultFontSize
        {
            get
            {
                try
                {
                    if (double.TryParse(_rawValues["DefaultFontSize"] as string, out double doubleTemp))
                    {
                        return doubleTemp;
                    }
                }
                catch
                {
                }

                this.DefaultFontSize = 12.0;
                return this.DefaultFontSize;
            }

            set => this.SetRawValue("DefaultFontSize", value.ToString());
        }

        public IList<ConfigRule> Rules { get => _rules; set => this.SetValue(ref _rules, value, "Rules"); }

        public static bool Save()
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Title = "Save config file",
                    Filter = "Json files (*.json)|*.json|All files (*.*)|*.*"
                };
                bool? saveFileDialogResult = saveFileDialog.ShowDialog();
                if (saveFileDialogResult.HasValue && saveFileDialogResult.Value)
                {
                    _filePath = saveFileDialog.FileName;
                }
            }

            if (!string.IsNullOrEmpty(_filePath))
            {
                try
                {
                    string jsonString = JsonConvert.SerializeObject(Config.This);
                    File.WriteAllText(_filePath, jsonString, System.Text.Encoding.UTF8);
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }

        public static void Reload()
        {
            _this = null;

            _filePath = Properties.Settings.Default.ConfigPath;
            if (Path.GetFileName(_filePath).ToLower() == "config.json" && File.Exists(Path.Combine(Path.GetDirectoryName(_filePath), "JsonViewer.exe")))
            {
                _filePath = null;
            }

            if (!File.Exists(_filePath))
            {
                _filePath = null;
            }

            if (string.IsNullOrEmpty(_filePath) && File.Exists("Config.json"))
            {
                _filePath = "Config.json";
            }

            if (!string.IsNullOrEmpty(_filePath))
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("Error processing config:\r\n");

                List<string> exceptionMessages = new List<string>();

                JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
                {
                    Error = new EventHandler<Newtonsoft.Json.Serialization.ErrorEventArgs>(
                    (obj, args) =>
                    {
                        exceptionMessages.Add(args.ErrorContext.Error.ToString());
                        args.ErrorContext.Handled = true;
                    }),
                    CheckAdditionalContent = true,
                    MaxDepth = 100,
                    MissingMemberHandling = MissingMemberHandling.Error
                };

                string errorMessage = string.Empty;
                try
                {
                    _this = JsonConvert.DeserializeObject<Config>(File.ReadAllText(_filePath), jsonSerializerSettings);
                    _this.IsDefault = false;

                    if (exceptionMessages.Count > 0)
                    {
                        errorMessage = "Exceptions hit while procesing config:\r\n\r\n";
                        errorMessage += string.Join("\r\n\r\n-------------------------\r\n\r\n", exceptionMessages.ToArray());
                    }
                }
                catch
                {
                    _this = null;
                    _this = new Config();
                    errorMessage = "Unable to process the config file.";
                }

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    MessageBox.Show(errorMessage, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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
            string key = configValue.ToString();

            try
            {
                if (!_colors.ContainsKey(configValue))
                {
                    _colors[configValue] = (Color)ColorConverter.ConvertFromString(_rawValues[key] as string);
                }

                return _colors[configValue];
            }
            catch
            {
            }

            Color fallback = Colors.Transparent;
            switch (configValue)
            {
                case ConfigValue.DefaultForeground:
                case ConfigValue.SelectedForeground:
                case ConfigValue.SearchResultForeground:
                case ConfigValue.SimilarNodeForeground:
                case ConfigValue.SelectedParentForeground:
                    fallback = Colors.Black;
                    break;
                case ConfigValue.SelectedBackground:
                    fallback = Colors.Yellow;
                    break;
                case ConfigValue.SearchResultBackground:
                    fallback = Colors.LightGreen;
                    break;
                case ConfigValue.DefaultBackground:
                case ConfigValue.SimilarNodeBackground:
                case ConfigValue.SelectedParentBackground:
                    fallback = Colors.White;
                    break;
                default:
                    Debug.Assert(false);
                    break;
            }

            _rawValues[key] = fallback.GetName();
            return fallback;
        }

        public Brush GetForegroundColor(JsonObject obj)
        {
            IRule rule = obj.Rules.FirstOrDefault(x => x.ForegroundBrush != null);
            if (rule != null)
            {
                return rule.ForegroundBrush;
            }

            return Config.This.GetBrush(ConfigValue.DefaultForeground);
        }

        public Brush GetBackgroundColor(JsonObject obj)
        {
            IRule rule = obj.Rules.FirstOrDefault(x => x.BackgroundBrush != null);
            if (rule != null)
            {
                return rule.BackgroundBrush;
            }

            return this.GetBrush(ConfigValue.DefaultBackground);
        }

        public double GetFontSize(JsonObject obj)
        {
            IRule rule = obj.Rules.FirstOrDefault(x => x.FontSize.HasValue);
            if (rule != null)
            {
                return rule.FontSize.Value;
            }

            return this.DefaultFontSize;
        }

        private string GetRawValue(string key, string fallback)
        {
            string result = null;
            if (_rawValues.ContainsKey(key))
            {
                result = _rawValues[key] as string;
            }

            if (string.IsNullOrEmpty(result))
            {
                result = fallback;
            }

            return result;
        }

        private void SetRawValue(string key, string value)
        {
            if (!_rawValues.ContainsKey(key) || _rawValues[key] as string != value)
            {
                _rawValues[key] = value;
                this.FirePropertyChanged(key);
            }
        }
    }
}
