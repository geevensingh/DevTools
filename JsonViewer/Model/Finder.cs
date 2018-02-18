namespace JsonViewer
{
    using System.Diagnostics;
    using System.Windows;

    public class Finder : NotifyPropertyChanged
    {
        private Window _parentWindow;
        private RootObject _rootObject = null;
        private string _text = Properties.Settings.Default.FindText;
        private bool _shouldSearchKeys = Properties.Settings.Default.FindSearchKeys;
        private bool _shouldSearchValues = Properties.Settings.Default.FindSearchValues;
        private bool _shouldSearchParentValues = Properties.Settings.Default.FindSearchParentValues;
        private bool _shouldIgnoreCase = Properties.Settings.Default.FindIgnoreCase;
        private FindWindow _findWindow = null;
        private int _hitCount = 0;

        public Finder(Window parentWindow)
        {
            _parentWindow = parentWindow;
        }

        public bool ShouldSearchKeys
        {
            get => _shouldSearchKeys;
            set
            {
                if (this.SetValue(ref _shouldSearchKeys, value, "ShouldSearchKeys"))
                {
                    Properties.Settings.Default.FindSearchKeys = _shouldSearchKeys;
                    Properties.Settings.Default.Save();

                    Update();
                }
            }
        }

        public bool ShouldSearchValues
        {
            get => _shouldSearchValues;
            set
            {
                if (this.SetValue(ref _shouldSearchValues, value, "ShouldSearchValues"))
                {
                    Properties.Settings.Default.FindSearchValues = _shouldSearchValues;
                    Properties.Settings.Default.Save();

                    Update();
                }
            }
        }

        public bool ShouldSearchParentValues
        {
            get => _shouldSearchParentValues;
            set
            {
                if (this.SetValue(ref _shouldSearchParentValues, value, "ShouldSearchParentValues"))
                {
                    Properties.Settings.Default.FindSearchParentValues = _shouldSearchParentValues;
                    Properties.Settings.Default.Save();

                    Update();
                }
            }
        }

        public bool ShouldIgnoreCase
        {
            get => _shouldIgnoreCase;
            set
            {
                if (this.SetValue(ref _shouldIgnoreCase, value, "ShouldIgnoreCase"))
                {
                    Properties.Settings.Default.FindIgnoreCase = _shouldIgnoreCase;
                    Properties.Settings.Default.Save();

                    Update();
                }
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                if (this.SetValue(ref _text, value, "Text"))
                {
                    Properties.Settings.Default.FindText = _text;
                    Properties.Settings.Default.Save();

                    Update();
                }
            }
        }

        public int HitCount { get => _hitCount; }

        public bool HasWindow { get => _findWindow != null; }

        public void ShowWindow()
        {
            this.HideWindow();

            Debug.Assert(_findWindow == null);
            _findWindow = new FindWindow(_parentWindow, this);
            _findWindow.Show();
            this.FirePropertyChanged("HasWindow");
        }

        public void HideWindow()
        {
            if (_findWindow != null)
            {
                _findWindow.Close();
                _findWindow = null;
                this.FirePropertyChanged("HasWindow");
            }
        }

        internal void SetObjects(RootObject rootObject)
        {
            _rootObject = rootObject;
            Update();
        }

        private void Update()
        {
            int count = 0;
            if (_rootObject != null)
            {
                Highlight(_rootObject, ref count);
            }

            this.SetValue(ref _hitCount, count, "HitCount");
        }

        private void Highlight(JsonObject obj, ref int count)
        {
            bool found = false;
            if (!string.IsNullOrEmpty(_text))
            {
                bool shouldSearchValue = obj.HasChildren ? _shouldSearchParentValues : _shouldSearchValues;
                if (_shouldSearchKeys && this.CompareStrings(obj.Key, _text))
                {
                    found = true;
                }
                else if (obj.HasChildren ? _shouldSearchParentValues : _shouldSearchValues)
                {
                    found = this.CompareStrings(obj.ValueString, _text);
                }
            }

            if (found)
            {
                count++;
            }

            obj.IsFindMatch = found;

            if (obj.HasChildren)
            {
                foreach (JsonObject child in obj.Children)
                {
                    this.Highlight(child, ref count);
                }
            }
        }

        private bool CompareStrings(string text, string substring)
        {
            if (_shouldIgnoreCase)
            {
                return text.ToLower().Contains(substring.ToLower());
            }

            return text.Contains(substring);
        }
    }
}
