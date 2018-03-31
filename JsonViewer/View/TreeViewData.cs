namespace JsonViewer.View
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Windows.Controls;
    using System.Windows.Media;
    using JsonViewer.Commands.PerItem;
    using JsonViewer.Model;
    using JsonViewer.ViewModel;
    using Utilities;

    public class TreeViewData : NotifyPropertyChanged
    {
        private ListView _tree = null;
        private VMObject _vmObject;
        private ObservableCollection<TreeViewData> _children = new ObservableCollection<TreeViewData>();
        private bool _isSelected = false;
        private bool _isChildSelected = false;

        internal TreeViewData(ListView tree, VMObject vmObject, IList<TreeViewData> children)
        {
            Debug.Assert(System.Threading.Thread.CurrentThread.ManagedThreadId == 1);

            _tree = tree;
            _vmObject = vmObject;
            _vmObject.Data.ViewObject = this;
            _children = new ObservableCollection<TreeViewData>(children);

            _vmObject.Data.PropertyChanged += OnDataModelPropertyChanged;
            _vmObject.Data.Rules.PropertyChanged += OnRulesPropertyChanged;
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;

            this.ExpandChildrenCommand = new ExpandChildrenCommand(this);
            this.ExpandAllCommand = new ExpandAllCommand(this);
            this.CollapseAllCommand = new CollapseAllCommand(this);
            this.CopyKeyCommand = new CopyKeyCommand(this);
            this.CopyValueCommand = new CopyValueCommand(this);
            this.CopyPrettyValueCommand = new CopyPrettyValueCommand(this);
            this.CopyEscapedValueCommand = new CopyEscapedValueCommand(this);
            this.TreatAsJsonCommand = new TreatAsJsonCommand(tree, this);
            this.TreatAsTextCommand = new TreatAsTextCommand(tree, this);
        }

        public string KeyName { get => _vmObject.Data.Key; }

        public string Value { get => _vmObject.Data.ValueString; }

        public string PrettyValue { get => _vmObject.Data.PrettyValueString; }

        public string OneLineValue { get => _vmObject.Data.OneLineValue; }

        public string ValueType { get => _vmObject.Data.ValueTypeString; }

        public ObservableCollection<TreeViewData> Children { get => _children; }

        public TreeViewData Parent { get => _vmObject.Data.Parent?.ViewObject; }

        public bool HasChildren { get => _vmObject.Data.HasChildren; }

        public int Depth { get => this.ParentList.Count * 20; }

        public IList<TreeViewData> ParentList
        {
            get
            {
                List<TreeViewData> parentList = new List<TreeViewData>();
                if (this.Parent != null)
                {
                    parentList.AddRange(this.Parent.ParentList);
                    parentList.Add(this.Parent);
                }

                return parentList;
            }
        }

        public Brush TextColor
        {
            get
            {
                if (this._isSelected)
                {
                    return Config.Values.GetBrush(ConfigValue.SelectedForeground);
                }

                if (_isChildSelected && Properties.Settings.Default.HighlightSelectedParents)
                {
                    return Config.Values.GetBrush(ConfigValue.SelectedParentForeground);
                }

                return _vmObject.Data.Rules.TextColor;
            }
        }

        public Brush BackgroundColor
        {
            get
            {
                if (this._isSelected)
                {
                    return Config.Values.GetBrush(ConfigValue.SelectedBackground);
                }

                if (_isChildSelected && Properties.Settings.Default.HighlightSelectedParents)
                {
                    return Config.Values.GetBrush(ConfigValue.SelectedParentBackground);
                }

                return _vmObject.Data.Rules.BackgroundColor;
            }
        }

        public double FontSize
        {
            get
            {
                return _vmObject.Data.Rules.FontSize;
            }
        }

        public bool IsChildSelected
        {
            get
            {
                return _isChildSelected;
            }

            set
            {
                if (this.SetValue(ref _isChildSelected, value, new string[] { "BackgroundColor", "TextColor" }))
                {
                    if (this.Parent != null)
                    {
                        this.Parent.IsChildSelected = _isChildSelected;
                    }
                }
            }
        }

        public bool IsSelected
        {
            get
            {
                return _isSelected;
            }

            set
            {
                if (this.SetValue(ref _isSelected, value, new string[] { "BackgroundColor", "TextColor" }))
                {
                    if (this.Parent != null)
                    {
                        this.Parent.IsChildSelected = _isSelected;
                    }
                }
            }
        }

        public ExpandChildrenCommand ExpandChildrenCommand { get; private set; }

        public ExpandAllCommand ExpandAllCommand { get; private set; }

        public CollapseAllCommand CollapseAllCommand { get; private set; }

        public CopyKeyCommand CopyKeyCommand { get; private set; }

        public CopyValueCommand CopyValueCommand { get; private set; }

        public CopyPrettyValueCommand CopyPrettyValueCommand { get; private set; }

        public CopyEscapedValueCommand CopyEscapedValueCommand { get; private set; }

        public TreatAsJsonCommand TreatAsJsonCommand { get; private set; }

        public TreatAsTextCommand TreatAsTextCommand { get; private set; }

        internal JsonObject JsonObject { get => _vmObject.Data; }

        internal VMObject VMObject { get => _vmObject; }

        internal ListView Tree { get => _tree; }

        private void OnDataModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "HasChildren":
                    this.FirePropertyChanged(new string[] { "HasChildren", "ValueType" });
                    break;
                case "TotalChildCount":
                    this.FirePropertyChanged(new string[] { "ValueType", "OneLineValue" });
                    break;
                case "ValueString":
                    this.FirePropertyChanged("Value");
                    break;
                case "Children":
                case "AllChildren":
                    this.FirePropertyChanged("AllChildren");
                    break;
                default:
                    Debug.Assert(false, "Unknown property change");
                    break;
            }
        }

        private void OnRulesPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "TextColor":
                case "BackgroundColor":
                case "FontSize":
                    this.FirePropertyChanged(e.PropertyName);
                    break;
                case "Rules":
                case "FindRule":
                case "MatchRule":
                case "ExpandChildren":
                case "WarningMessages":
                    break;
                default:
                    Debug.Assert(false, "Unknown property change");
                    break;
            }
        }

        private void OnSettingsPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "HighlightSelectedParents" && _isChildSelected)
            {
                this.FirePropertyChanged(new string[] { "TextColor", "BackgroundColor" });
            }
        }
    }
}
