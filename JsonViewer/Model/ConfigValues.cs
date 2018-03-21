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
