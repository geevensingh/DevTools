namespace JsonViewer.Model
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Controls;
    using JsonViewer.View;
    using Utilities;

    public class RuleSet : NotifyPropertyChanged
    {
        private ObservableCollection<RuleView> _rules;
        bool _isReordering = false;

        public RuleSet()
        {
        }

        public ObservableCollection<RuleView> Rules
        {
            get
            {
                if (_rules == null)
                {
                    this.SetRules(RuleViewFactory.CreateCollection(this));
                }

                return _rules;
            }

            set
            {
                this.SetRules(value);
            }
        }

        private void OnRulesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Debug.Assert(e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Move);
            Debug.Assert(e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Remove);
            Debug.Assert(e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Reset);
        }

        private void SetRules(ObservableCollection<RuleView> rules)
        {
            if (rules == _rules)
            {
                return;
            }

            if (_rules != null)
            {
                _rules.CollectionChanged -= OnRulesCollectionChanged;
            }

            if (rules != null)
            {
                rules.CollectionChanged += OnRulesCollectionChanged;
            }

            _rules = rules;
            this.FirePropertyChanged("Rules");
        }

        internal void Refresh()
        {
            this.SetRules(null);
        }

        internal void AddNewRule(InitializingNewItemEventArgs e)
        {
            RuleView newRuleView = (RuleView)e.NewItem;
            newRuleView.RuleSet = this;
            newRuleView.Index = _rules.Max((rule) => rule.Index) + 1;
        }

        internal void SetIndex(RuleView ruleView, int newIndex)
        {
            Debug.Assert(_isReordering == false);

            _isReordering = true;
            if (newIndex >= _rules.Count)
            {
                newIndex = _rules.Count - 1;
            }

#if DEBUG
            for (int ii = 0; ii < _rules.Count; ii++)
            {
                Debug.Assert(_rules[ii].Index == ii || _rules[ii].Index == -1);
            }
#endif
            int oldIndex = _rules.IndexOf(ruleView);
            if (oldIndex != newIndex)
            {
                Debug.Assert(ruleView.Index == oldIndex);

                if (oldIndex == -1)
                {
                    Debug.Assert(newIndex == _rules.Count);
                }
                else
                {
                    _rules.Move(oldIndex, newIndex);

                    for (int ii = Math.Min(oldIndex, newIndex); ii < _rules.Count; ii++)
                    {
                        _rules[ii].UpdateIndex(ii);
                    }
                }
            }

            _isReordering = false;
        }
    }
}
