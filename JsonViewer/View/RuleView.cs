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
            get => _rule.FontSize.ToString();
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

        public int? ExpandChildren
        {
            get => _rule.ExpandChildren;
            set
            {
                _rule.ExpandChildren = value;
                this.SetValue(ref _isDirty, true, "IsDirty");
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

        public Brush Background { get => _rule.BackgroundBrush ?? Config.This.DefaultBackgroundBrush; }

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
    }
}
