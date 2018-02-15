namespace JsonViewer
{
    using System.Windows;

    internal class Finder
    {
        private Window _parentWindow;
        private RootObject _rootObject = null;
        private string _text = string.Empty;
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

        public bool ShouldSearchKeys { get => _shouldSearchKeys; }

        public bool ShouldSearchValues { get => _shouldSearchValues; }

        public bool ShouldSearchParentValues { get => _shouldSearchParentValues; }

        public bool ShouldIgnoreCase { get => _shouldIgnoreCase; }

        public string Text { get => _text; }

        public int HitCount { get => _hitCount; }

        public void ShowWindow()
        {
            this.HideWindow();

            _findWindow = new FindWindow(_parentWindow, this);
            _findWindow.FindTextChanged += OnFindTextChanged;
            _findWindow.FindOptionsChanged += OnFindOptionsChanged;
            _findWindow.Show();
        }

        public void HideWindow()
        {
            if (_findWindow != null)
            {
                _findWindow.Close();
                _findWindow = null;
            }
        }

        public void SetObjects(RootObject rootObject)
        {
            _rootObject = rootObject;
        }

        private void OnFindOptionsChanged()
        {
            _shouldSearchKeys = _findWindow.ShouldSearchKeys;
            _shouldSearchValues = _findWindow.ShouldSearchValues;
            _shouldSearchParentValues = _findWindow.ShouldSearchParentValues;
            _shouldIgnoreCase = _findWindow.ShouldIgnoreCase;
            Update();
        }

        private void OnFindTextChanged(string oldText, string newText)
        {
            _text = newText;
            Update();
        }

        private void Update()
        {
            int count = 0;
            Highlight(_rootObject, ref count);
            _hitCount = count;
            _findWindow.SetHitCount(count);
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
