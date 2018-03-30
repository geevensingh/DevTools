using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JsonViewer.Model;
using JsonViewer.View;
using Utilities;

namespace JsonViewer.ViewModel
{
    public class VMObject : NotifyPropertyChanged
    {
        private RuleSet _rules = null;
        private JsonObject _jsonObject = null;

        public VMObject(JsonObject jsonObject)
        {
            _jsonObject = jsonObject;
            _jsonObject.PropertyChanged += OnJsonObjectPropertyChanged;
        }

        private void OnJsonObjectPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Debug.Assert(sender == _jsonObject);
            switch (e.PropertyName)
            {
                case "Values":
                    if (_rules == null)
                    {
                        _rules = new RuleSet(_jsonObject);
                    }

                    this.ApplyRules();
                    break;
                default:
                    break;
            }
        }

        protected virtual void ApplyRules()
        {
            _rules.Initialize();
        }


        internal RuleSet Rules
        {
            get
            {
                _jsonObject.EnsureValues();
                return _rules;
            }
        }

        internal FindRule FindRule
        {
            get => _rules.FindRule;
            set
            {
                _jsonObject.EnsureValues();
                _rules.SetFindRule(value);
            }
        }

        internal FindRule MatchRule
        {
            get => _rules.MatchRule;
            set
            {
                _jsonObject.EnsureValues();
                _rules.SetMatchRule(value);
            }
        }

        public JsonObject JsonObject { get => _jsonObject; }
    }
}
