namespace JsonViewer.Model
{
    using System.Diagnostics;
    using System.Windows.Media;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

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

    [JsonObject(MemberSerialization.OptIn)]
    public class ConfigRule : IRule
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

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string String { get; set; }

        [JsonProperty]
        [JsonConverter(typeof(StringEnumConverter))]
        public MatchTypeEnum MatchType { get; set; }

        [JsonProperty]
        [JsonConverter(typeof(StringEnumConverter))]
        public MatchFieldEnum MatchField { get; set; }

        [JsonProperty]
        public bool AppliesToParents { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string ForegroundString
        {
            get => _foregroundString;
            set
            {
                _foregroundString = value;
                _foregroundBrush = null;
            }
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
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
                    FileLogger.Assert(_foregroundBrush != null);
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
                    FileLogger.Assert(_backgroundBrush != null);
                }

                return _backgroundBrush;
            }
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public double? FontSize
        {
            get => _fontSize;
            set => _fontSize = value;
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? ExpandChildren { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string WarningMessage { get; set; }

        [JsonProperty]
        public bool IgnoreCase { get; set; }

        public ConfigRule Clone()
        {
            return (ConfigRule)this.MemberwiseClone();
        }

        public bool Matches(Json.JsonObject obj)
        {
            if (_matcher == null)
            {
                _matcher = new ConfigRuleMatcher(this);
            }

            return _matcher.Matches(obj);
        }
    }
}
