namespace JsonViewer.Model
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Windows.Media;
    using Utilities;

    public enum MatchTypeEnum
    {
        Exact,
        Partial
    }

    public enum MatchFieldEnum
    {
        Key,
        Type,
        Value
    }

    public class ConfigRule
    {
        private ConfigRuleMatcher _matcher = null;

        private string _foregroundString = string.Empty;
        private Brush _foregroundBrush = null;
        private string _backgroundString = string.Empty;
        private Brush _backgroundBrush = null;
        private double? _fontSize = null;

        public ConfigRule()
        {
            this.String = string.Empty;
            AppliesToParents = false;
            ForegroundString = null;
            BackgroundString = null;
            ExpandChildren = null;
            WarningMessage = null;
            IgnoreCase = false;
        }

        public string String { get; set; }

        public MatchTypeEnum MatchType { get; set; }

        public MatchFieldEnum MatchField { get; set; }

        public bool AppliesToParents { get; set; }

        public string ForegroundString
        {
            get => _foregroundString;
            set
            {
                _foregroundString = value;
                _foregroundBrush = null;
            }
        }

        public string BackgroundString
        {
            get => _backgroundString;
            set
            {
                _backgroundString = value;
                _backgroundBrush = null;
            }
        }

        public Brush ForegroundBrush
        {
            get
            {
                if (_foregroundBrush == null && !string.IsNullOrEmpty(this.ForegroundString))
                {
                    _foregroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(this.ForegroundString));
                    Debug.Assert(_foregroundBrush != null);
                }

                return _foregroundBrush;
            }
        }

        public Brush BackgroundBrush
        {
            get
            {
                if (_backgroundBrush == null && !string.IsNullOrEmpty(this.BackgroundString))
                {
                    _backgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(this.BackgroundString));
                    Debug.Assert(_backgroundBrush != null);
                }

                return _backgroundBrush;
            }
        }

        public double? FontSize
        {
            get => _fontSize;
            set => _fontSize = value;
        }

        public int? ExpandChildren { get; set; }

        public string WarningMessage { get; set; }

        public bool IgnoreCase { get; set; }

        public bool Matches(JsonObject obj)
        {
            if (_matcher == null)
            {
                _matcher = new ConfigRuleMatcher(this);
            }

            return _matcher.Matches(obj);
        }
    }
}
