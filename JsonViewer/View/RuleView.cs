namespace JsonViewer.View
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using JsonViewer.Model;
    using Utilities;

    public class RuleView : NotifyPropertyChanged
    {
        private ConfigRule _rule;

        internal RuleView(ConfigRule rule)
        {
            _rule = rule;
            this.Description = "akwduhkawudhwakdu";
        }

        public string Description { get; }
    }
}
