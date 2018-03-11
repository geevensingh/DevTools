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

        internal RuleView(ConfigRule rule, int index)
        {
            _rule = rule;
            this.Index = index;
        }

        public int Index { get; private set; }

        public string KeyMatchString
        {
            get
            {
                return DescribeMatch("Key ", _rule.ExactKeys, _rule.PartialKeys);
            }
        }

        public string ValueTypeMatchString
        {
            get
            {
                return DescribeMatch("Value type ", _rule.ExactValueTypes, _rule.PartialValueTypes);
            }
        }

        public string ValueMatchString
        {
            get
            {
                return DescribeMatch("Value ", _rule.ExactValues, _rule.PartialValues);
            }
        }

        public bool IgnoreCase { get => _rule.IgnoreCase; }

        public bool AppliesToParents { get => _rule.AppliesToParents; }

        public double? ExpandChildren { get => _rule.ExpandChildren; }

        public string WarningMessage
        {
            get
            {
                return _rule.WarningMessage;
            }
        }

        public Brush Background
        {
            get
            {
                return _rule.BackgroundBrush ?? Config.This.DefaultBackgroundBrush;
            }
        }

        public Brush Foreground
        {
            get
            {
                return _rule.ForegroundBrush ?? Config.This.DefaultForegroundBrush;
            }
        }

        public string BackgroundString { get => _rule.BackgroundString ?? Config.This.DefaultBackgroundString; }

        public string ForegroundString { get => _rule.ForegroundString ?? Config.This.DefaultForegroundString; }

        public string ColorString
        {
            get
            {
                return this.ForegroundString + " on " + this.BackgroundString;
            }
        }

        public double FontSize { get => _rule.FontSize ?? Config.This.DefaultFontSize; }

        public static int GetIndex(string columnName)
        {
            switch (columnName)
            {
                case "Index":
                    return 0;
                case "KeyMatchString":
                    return 1;
                case "ValueTypeMatchString":
                    return 2;
                case "ValueMatchString":
                    return 3;
                case "IgnoreCase":
                    return 4;
                case "AppliesToParents":
                    return 5;
                case "Color":
                    return 6;
                case "ExpandChildren":
                    return 7;
                case "WarningMessage":
                    return 8;
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
