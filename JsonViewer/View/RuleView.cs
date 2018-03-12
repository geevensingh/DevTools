namespace JsonViewer.View
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Media;
    using JsonViewer.Model;
    using Utilities;

    public class RuleView : NotifyPropertyChanged
    {
        private ConfigRule _rule;
        private bool _isDirty = false;

        public RuleView()
        {
            _rule = new ConfigRule();
        }

        internal RuleView(ConfigRule rule, int index)
        {
            _rule = rule;
            this.Index = index;
        }

        public ConfigRule Rule { get => _rule; }

        public int Index { get; set; }

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

        public string FontSize
        {
            get => _rule.FontSize.HasValue ? _rule.FontSize.ToString() : string.Empty;
            set
            {
                double? newValue = _rule.FontSize;
                if (string.IsNullOrEmpty(value))
                {
                    newValue = null;
                }

                if (double.TryParse(value, out double doubleTemp) && doubleTemp >= 4 && doubleTemp <= 48)
                {
                    newValue = doubleTemp;
                }

                if (newValue != _rule.FontSize)
                {
                    _rule.FontSize = newValue;
                    this.FirePropertyChanged("FontSize");
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

        public string SetForegroundText { get => _rule.ForegroundBrush == null ? "Set foreground" : "Clear foreground"; }

        public string SetBackgroundText { get => _rule.BackgroundBrush == null ? "Set background" : "Clear background"; }

        public Brush Background { get => _rule.BackgroundBrush ?? Config.This.DefaultBackgroundBrush; }

        public Brush BackgroundOrDefaultForeground { get => _rule.BackgroundBrush ?? Config.This.DefaultForegroundBrush; }

        public Brush Foreground { get => _rule.ForegroundBrush ?? Config.This.DefaultForegroundBrush; }

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

        public static int GetIndex(string columnName)
        {
            switch (columnName)
            {
                case "Index":
                    return 0;
                case "MatchString":
                    return 1;
                case "MatchType":
                    return 2;
                case "MatchField":
                    return 3;
                case "IgnoreCase":
                    return 4;
                case "AppliesToParents":
                    return 5;
                case "Color":
                    return 6;
                case "FontSize":
                    return 7;
                case "ExpandChildren":
                    return 8;
                case "WarningMessage":
                    return 9;
                default:
                    Debug.Fail("Unknown column name: " + columnName);
                    return -1;
            }
        }

        private static string DescribeMatch(string prefix, IList<string> exact, IList<string> partial)
        {
            List<string> list = new List<string>();
            list.AddRange(exact.Select(x => "\"" + x + "\""));
            list.AddRange(partial.Select(x => "\"*" + x + "*\""));

            if (list.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(" or ", list.ToArray());
        }

        internal void SetForeground()
        {
            if (string.IsNullOrEmpty(_rule.ForegroundString))
            {
                // launch color picker
            }
            else
            {
                _rule.ForegroundString = string.Empty;
            }

            this.FirePropertyChanged(new string[] { "SetForegroundText", "Foreground", "ColorString" });
        }

        internal void SetBackground()
        {
        }
    }
}
