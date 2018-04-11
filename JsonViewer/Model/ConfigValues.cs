namespace JsonViewer.Model
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Media;
    using Microsoft.Win32;
    using Newtonsoft.Json;
    using Utilities;

    [JsonObject]
    public class ConfigValues : NotifyPropertyChanged
    {
        private Dictionary<string, string> _rawValues = new Dictionary<string, string>();
        private Dictionary<ConfigValue, Color> _colors = new Dictionary<ConfigValue, Color>();
        private Dictionary<ConfigValue, Brush> _brushes = new Dictionary<ConfigValue, Brush>();
        private IList<ConfigRule> _rules = new List<ConfigRule>();

        public string DefaultForeground { get => this.GetRawValue(ConfigValue.DefaultForeground); set => this.SetColorString(ConfigValue.DefaultForeground, value); }

        public string DefaultBackground { get => this.GetRawValue(ConfigValue.DefaultBackground); set => this.SetColorString(ConfigValue.DefaultBackground, value); }

        public string SelectedForeground { get => this.GetRawValue(ConfigValue.SelectedForeground); set => this.SetColorString(ConfigValue.SelectedForeground, value); }

        public string SelectedBackground { get => this.GetRawValue(ConfigValue.SelectedBackground); set => this.SetColorString(ConfigValue.SelectedBackground, value); }

        public string SearchResultForeground { get => this.GetRawValue(ConfigValue.SearchResultForeground); set => this.SetColorString(ConfigValue.SearchResultForeground, value); }

        public string SearchResultBackground { get => this.GetRawValue(ConfigValue.SearchResultBackground); set => this.SetColorString(ConfigValue.SearchResultBackground, value); }

        public string SimilarNodeForeground { get => this.GetRawValue(ConfigValue.SimilarNodeForeground); set => this.SetColorString(ConfigValue.SimilarNodeForeground, value); }

        public string SimilarNodeBackground { get => this.GetRawValue(ConfigValue.SimilarNodeBackground); set => this.SetColorString(ConfigValue.SimilarNodeBackground, value); }

        public string SelectedParentForeground { get => this.GetRawValue(ConfigValue.SelectedParentForeground); set => this.SetColorString(ConfigValue.SelectedParentForeground, value); }

        public string SelectedParentBackground { get => this.GetRawValue(ConfigValue.SelectedParentBackground); set => this.SetColorString(ConfigValue.SelectedParentBackground, value); }

        public double DefaultFontSize
        {
            get
            {
                try
                {
                    string valueString = this.GetRawValue("DefaultFontSize");
                    if (double.TryParse(valueString, out double valueDouble))
                    {
                        return valueDouble;
                    }
                }
                catch
                {
                }

                return 12.0;
            }

            set
            {
                double newValue = MathHelper.Clamp(value, 8.0, 48.0);
                this.SetRawValue("DefaultFontSize", newValue.ToString());
            }
        }

        public IList<ConfigRule> Rules { get => _rules; set => this.SetValue(ref _rules, value, "Rules"); }

        public ConfigValues Clone()
        {
            return JsonConvert.DeserializeObject<ConfigValues>(JsonConvert.SerializeObject(this));
        }

        public async Task<bool> Save(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Title = "Save config file",
                    Filter = "Json files (*.json)|*.json|All files (*.*)|*.*"
                };
                bool? saveFileDialogResult = saveFileDialog.ShowDialog();
                if (saveFileDialogResult.HasValue && saveFileDialogResult.Value)
                {
                    filePath = saveFileDialog.FileName;
                }
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                string jsonString = string.Empty;
                try
                {
                    jsonString = JsonConvert.SerializeObject(this);
                }
                catch
                {
                    return false;
                }

                try
                {
                    DeserializeResult deserializeResult = await JsonObjectFactory.TrySimpleDeserialize(jsonString);
                    FileLogger.Assert(deserializeResult == null || !deserializeResult.HasExtraText);
                    RootObject rootObject = await RootObject.Create(deserializeResult?.Dictionary);
                    jsonString = rootObject.PrettyValueString;
                }
                catch
                {
                    // Don't care if this fails
                }

                try
                {
                    File.WriteAllText(filePath, jsonString, Encoding.UTF8);
                    return true;
                }
                catch
                {
                    // Falls through to return false
                }
            }

            return false;
        }

        public Brush GetBrush(ConfigValue configValue)
        {
            if (!_brushes.ContainsKey(configValue))
            {
                Debug.WriteLine("thread id: " + System.Threading.Thread.CurrentThread.ManagedThreadId);
                _brushes[configValue] = new SolidColorBrush(this.GetColorWithFallback(configValue));
            }

            return _brushes[configValue];
        }

        public Color GetColorWithFallback(ConfigValue configValue)
        {
            Color color = this.GetColor(configValue);
            if (color == Colors.Transparent)
            {
                switch (configValue)
                {
                    case ConfigValue.DefaultForeground:
                    case ConfigValue.SelectedForeground:
                    case ConfigValue.SimilarNodeForeground:
                    case ConfigValue.SelectedParentForeground:
                        color = Colors.Black;
                        break;
                    case ConfigValue.SearchResultForeground:
                        color = Colors.Blue;
                        break;
                    case ConfigValue.DefaultBackground:
                        color = Colors.White;
                        break;
                    case ConfigValue.SelectedBackground:
                        color = Colors.Yellow;
                        break;
                    case ConfigValue.SearchResultBackground:
                        color = Colors.LightGreen;
                        break;
                    case ConfigValue.SimilarNodeBackground:
                        color = Colors.LightBlue;
                        break;
                    case ConfigValue.SelectedParentBackground:
                        color = Colors.LightGoldenrodYellow;
                        break;
                    default:
                        FileLogger.Assert(false);
                        break;
                }
            }

            return color;
        }

        public Color GetColor(ConfigValue configValue)
        {
            try
            {
                if (!_colors.ContainsKey(configValue))
                {
                    string colorString = this.GetRawValue(configValue);
                    _colors[configValue] = (Color)ColorConverter.ConvertFromString(colorString);
                }

                return _colors[configValue];
            }
            catch
            {
            }

            return Colors.Transparent;
        }

        public void SetColorString(ConfigValue configValue, string colorString)
        {
            Color color = Colors.Transparent;
            if (!string.IsNullOrEmpty(colorString))
            {
                color = (Color)ColorConverter.ConvertFromString(colorString);
            }

            this.SetColor(configValue, color);
        }

        public void SetColor(ConfigValue configValue, Color color)
        {
            string colorString = color.GetName();
            if (color == Colors.Transparent)
            {
                colorString = string.Empty;
            }

            this.SetRawValue(
                configValue,
                colorString,
                () =>
                {
                    _colors.Remove(configValue);
                    _brushes.Remove(configValue);
                });
        }

        private string GetRawValue(ConfigValue configValue)
        {
            return this.GetRawValue(configValue.ToString());
        }

        private string GetRawValue(string key)
        {
            if (_rawValues.ContainsKey(key))
            {
                return _rawValues[key];
            }

            return string.Empty;
        }

        private bool SetRawValue(ConfigValue configValue, string value, Action action = null)
        {
            return this.SetRawValue(configValue.ToString(), value, action);
        }

        private bool SetRawValue(string key, string value, Action action = null)
        {
            bool hasChanged = false;
            if (!_rawValues.ContainsKey(key))
            {
                _rawValues[key] = value;
                hasChanged = true;
            }
            else if (string.IsNullOrEmpty(value))
            {
                _rawValues.Remove(key);
                hasChanged = true;
            }
            else if (_rawValues[key] != value)
            {
                _rawValues[key] = value;
                hasChanged = true;
            }

            if (hasChanged)
            {
                action?.Invoke();
                this.FirePropertyChanged(key);
            }

            return hasChanged;
        }
    }
}
