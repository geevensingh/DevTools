namespace JsonViewer.View
{
    using System;
    using System.Diagnostics;
    using System.Windows.Media;
    using JsonViewer.Model;
    using Utilities;

    public class EditableRuleView : NotifyPropertyChanged
    {
        private ConfigValues _configValues;
        private ConfigRule _rule;
        private bool _isDirty = false;
        private int _index = -1;

        public EditableRuleView()
        {
            Debug.Assert(System.Threading.Thread.CurrentThread.ManagedThreadId == 1);

            _rule = new ConfigRule();
        }

        internal EditableRuleView(ConfigRule rule, int index, EditableRuleSet ruleSet, ConfigValues configValues)
        {
            _configValues = configValues;
            _configValues.PropertyChanged += OnConfigValuesPropertyChanged;
            _rule = rule.Clone();
            this.UpdateIndex(index);
            this.RuleSet = ruleSet;
        }

        public ConfigRule Rule { get => _rule.Clone(); }

        public int Index
        {
            get => _index;
            set
            {
                if (_index != value)
                {
                    this.UpdateIndex(this.RuleSet.SetIndex(this, value));
                }
            }
        }

        public bool IsDirty
        {
            get => _isDirty;
            set => this.SetValue(ref _isDirty, value, "IsDirty");
        }

        public string MatchString
        {
            get => _rule.String;
            set
            {
                _rule.String = value;
                this.SetValue(ref _isDirty, true, "IsDirty");
            }
        }

        public MatchTypeEnum MatchType
        {
            get => _rule.MatchType;
            set
            {
                _rule.MatchType = value;
                this.SetValue(ref _isDirty, true, "IsDirty");
            }
        }

        public MatchFieldEnum MatchField
        {
            get => _rule.MatchField;
            set
            {
                _rule.MatchField = value;
                this.SetValue(ref _isDirty, true, "IsDirty");
            }
        }

        public bool IgnoreCase
        {
            get => _rule.IgnoreCase;
            set
            {
                _rule.IgnoreCase = value;
                this.SetValue(ref _isDirty, true, "IsDirty");
            }
        }

        public double FontSize
        {
            get
            {
                if (_rule.FontSize.HasValue)
                {
                    return _rule.FontSize.Value;
                }

                return _configValues.DefaultFontSize;
            }
        }

        public string FontSizeString
        {
            get => _rule.FontSize.HasValue ? _rule.FontSize.ToString() : string.Empty;
            set
            {
                double? newValue = _rule.FontSize;
                if (string.IsNullOrEmpty(value))
                {
                    newValue = null;
                }

                if (double.TryParse(value, out double doubleTemp))
                {
                    newValue = Math.Max(4, Math.Min(48, doubleTemp));
                }

                if (newValue != _rule.FontSize)
                {
                    _rule.FontSize = newValue;
                    this.FirePropertyChanged(new string[] { "FontSize", "FontSizeString" });
                    this.SetValue(ref _isDirty, true, "IsDirty");
                }
            }
        }

        public bool AppliesToParents
        {
            get => _rule.AppliesToParents;
            set
            {
                _rule.AppliesToParents = value;
                this.SetValue(ref _isDirty, true, "IsDirty");
            }
        }

        public string ExpandChildren
        {
            get => _rule.ExpandChildren.ToString();
            set
            {
                int? newValue = _rule.ExpandChildren;
                if (string.IsNullOrEmpty(value))
                {
                    newValue = null;
                }

                if (int.TryParse(value, out int intTemp) && intTemp > 0)
                {
                    newValue = intTemp;
                }

                if (newValue != _rule.ExpandChildren)
                {
                    _rule.ExpandChildren = newValue;
                    this.SetValue(ref _isDirty, true, "IsDirty");
                }
            }
        }

        public string WarningMessage
        {
            get => _rule.WarningMessage;
            set
            {
                _rule.WarningMessage = value;
                this.SetValue(ref _isDirty, true, "IsDirty");
            }
        }

        public Brush BackgroundBrush { get => _rule.BackgroundBrush ?? _configValues.GetBrush(ConfigValue.DefaultBackground); }

        public Brush ForegroundBrush { get => _rule.ForegroundBrush ?? _configValues.GetBrush(ConfigValue.DefaultForeground); }

        public Color ForegroundColor
        {
            get => string.IsNullOrEmpty(_rule.ForegroundString) ? Colors.Transparent : (Color)ColorConverter.ConvertFromString(_rule.ForegroundString);
            set
            {
                string valueName = value.GetName();
                if (valueName.ToLower() == "transparent")
                {
                    valueName = null;
                }

                if (_rule.ForegroundString?.ToLower() != valueName?.ToLower())
                {
                    _rule.ForegroundString = value.GetName();
                    this.FirePropertyChanged(new string[] { "Foreground", "ForegroundBrush", "ColorString" });
                    this.SetValue(ref _isDirty, true, "IsDirty");
                }
            }
        }

        public Color BackgroundColor
        {
            get => string.IsNullOrEmpty(_rule.BackgroundString) ? Colors.Transparent : (Color)ColorConverter.ConvertFromString(_rule.BackgroundString);
            set
            {
                string valueName = value.GetName();
                if (valueName.ToLower() == "transparent")
                {
                    valueName = null;
                }

                if (_rule.BackgroundString?.ToLower() != valueName?.ToLower())
                {
                    _rule.BackgroundString = valueName;
                    this.FirePropertyChanged(new string[] { "Background", "BackgroundBrush", "ColorString" });
                    this.SetValue(ref _isDirty, true, "IsDirty");
                }
            }
        }

        public string ColorString
        {
            get
            {
                if (string.IsNullOrEmpty(_rule.ForegroundString) && string.IsNullOrEmpty(_rule.BackgroundString))
                {
                    return string.Empty;
                }

                return (_rule.ForegroundString ?? "default") + " on " + (_rule.BackgroundString ?? "default");
            }
        }

        public EditableRuleSet RuleSet { get; internal set; }

        internal void SetConfigValues(ConfigValues configValues)
        {
            FileLogger.Assert(_configValues == null);
            _configValues = configValues;
        }

        internal void UpdateIndex(int newIndex)
        {
            this.SetValue(ref _index, newIndex, "Index");
        }

        private void OnConfigValuesPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "DefaultFontSize":
                    this.FirePropertyChanged(new string[] { "FontSize", "FontSizeString" });
                    break;
                case "DefaultForeground":
                    this.FirePropertyChanged(new string[] { "Foreground", "ForegroundBrush", "ColorString" });
                    break;
                case "DefaultBackground":
                    this.FirePropertyChanged(new string[] { "Background", "BackgroundBrush", "ColorString" });
                    break;
                default:
                    break;
            }
        }
    }
}
