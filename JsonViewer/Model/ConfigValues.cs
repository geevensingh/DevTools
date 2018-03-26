namespace JsonViewer.Model
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Media;
    using Microsoft.Win32;
    using Newtonsoft.Json;
    using Utilities;

    [JsonObject]
    internal class ConfigValues : NotifyPropertyChanged
    {
        private Dictionary<string, object> _rawValues = new Dictionary<string, object>();
        private Dictionary<ConfigValue, Color> _colors = new Dictionary<ConfigValue, Color>();
        private Dictionary<ConfigValue, Brush> _brushes = new Dictionary<ConfigValue, Brush>();
        private IList<ConfigRule> _rules = new List<ConfigRule>();

        public Color DefaultForeground { get => this.GetColor(ConfigValue.DefaultForeground); set => this.SetColor(ConfigValue.DefaultForeground, value); }

        public Color DefaultBackground { get => this.GetColor(ConfigValue.DefaultBackground); set => this.SetColor(ConfigValue.DefaultBackground, value); }

        public Color SelectedForeground { get => this.GetColor(ConfigValue.SelectedForeground); set => this.SetColor(ConfigValue.SelectedForeground, value); }

        public Color SelectedBackground { get => this.GetColor(ConfigValue.SelectedBackground); set => this.SetColor(ConfigValue.SelectedBackground, value); }

        public Color SearchResultForeground { get => this.GetColor(ConfigValue.SearchResultForeground); set => this.SetColor(ConfigValue.SearchResultForeground, value); }

        public Color SearchResultBackground { get => this.GetColor(ConfigValue.SearchResultBackground); set => this.SetColor(ConfigValue.SearchResultBackground, value); }

        public Color SimilarNodeForeground { get => this.GetColor(ConfigValue.SimilarNodeForeground); set => this.SetColor(ConfigValue.SimilarNodeForeground, value); }

        public Color SimilarNodeBackground { get => this.GetColor(ConfigValue.SimilarNodeBackground); set => this.SetColor(ConfigValue.SimilarNodeBackground, value); }

        public Color SelectedParentForeground { get => this.GetColor(ConfigValue.SelectedParentForeground); set => this.SetColor(ConfigValue.SelectedParentForeground, value); }

        public Color SelectedParentBackground { get => this.GetColor(ConfigValue.SelectedParentBackground); set => this.SetColor(ConfigValue.SelectedParentBackground, value); }

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
                    jsonString = (await RootObject.Create(JsonObjectFactory.TryDeserialize(jsonString)?.Dictionary)).PrettyValueString;
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

        public void SetColor(ConfigValue configValue, Color color)
        {
            bool hasChanged = true;
            if (_colors.ContainsKey(configValue))
            {
                hasChanged = this.GetColor(configValue) != color;
            }

            if (hasChanged)
            {
                _colors[configValue] = color;
                _brushes.Remove(configValue);
                FirePropertyChanged(configValue.ToString());
            }
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
