namespace JsonViewer.View
{
    using System.Diagnostics;
    using System.Windows;
    using Utilities;

    public class FindMatchNavigator : NotifyPropertyChanged
    {
        private MainWindow _mainWindow;

        private int? _currentHitIndex = null;

        private string _findMatchText = string.Empty;

        public FindMatchNavigator(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;

            _mainWindow.Tree.PropertyChanged += OnTreePropertyChanged;
            _mainWindow.Finder.PropertyChanged += OnFinderPropertyChanged;

            UpdateFindMatches();
        }

        public enum Direction
        {
            Forward,
            Backward
        }

        public Visibility ShowFindControls { get => string.IsNullOrEmpty(_findMatchText) ? Visibility.Collapsed : Visibility.Visible; }

        public string FindMatchText { get => _findMatchText; }

        private int? CurrentIndex { get => _mainWindow.Tree.SelectedIndex; }

        public void Go(Direction direction)
        {
            Debug.Assert(_mainWindow.Finder.HitCount > 0);
            GetHitIndexRange(out int previous, out int next);

            int adjusted = direction == Direction.Forward ? (previous + 1) : (next - 1);
            _currentHitIndex = (adjusted + _mainWindow.Finder.Hits.Count) % _mainWindow.Finder.Hits.Count;
            JsonObject hit = _mainWindow.Finder.Hits[_currentHitIndex.Value];
            _mainWindow.Tree.SelectItem(hit.ViewObject);
        }

        private void OnTreePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Debug.Assert(sender == _mainWindow.Tree);
            switch (e.PropertyName)
            {
                case "SelectedIndex":
                    TreeViewData newSelectedData = (TreeViewData)_mainWindow.Tree.SelectedItem;
                    if (newSelectedData != null && newSelectedData.JsonObject.IsFindMatch)
                    {
                        _currentHitIndex = _mainWindow.Finder.Hits.IndexOf(newSelectedData.JsonObject);
                    }

                    UpdateFindMatches();
                    break;
            }
        }

        private void OnFinderPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Debug.Assert(sender == _mainWindow.Finder);
            switch (e.PropertyName)
            {
                case "Hits":
                    UpdateFindMatches();
                    break;
            }
        }

        private void UpdateFindMatches()
        {
            _currentHitIndex = null;

            Finder finder = _mainWindow.Finder;
            if (finder.HitCount == 0)
            {
                Debug.Assert(!_currentHitIndex.HasValue);
                this.SetValue(ref _findMatchText, string.Empty, new string[] { "ShowFindControls", "FindMatchText" });
                return;
            }

            int? currentIndex = this.CurrentIndex;
            if (currentIndex.HasValue)
            {
                JsonObject currentObj = _mainWindow.RootObject.AllChildren[currentIndex.Value];
                if (currentObj.IsFindMatch)
                {
                    int indexOfCurrentObj = finder.Hits.IndexOf(currentObj);
                    _currentHitIndex = indexOfCurrentObj == -1 ? (int?)null : indexOfCurrentObj;
                }
            }

            string findMatchText = $"?? / {finder.HitCount}";
            if (_currentHitIndex.HasValue)
            {
                findMatchText = $"{_currentHitIndex.Value + 1} / {finder.HitCount}";
            }

            this.SetValue(ref _findMatchText, findMatchText, new string[] { "ShowFindControls", "FindMatchText" });
        }

        private void GetHitIndexRange(out int previous, out int next)
        {
            if (_currentHitIndex.HasValue)
            {
                previous = _currentHitIndex.Value;
                next = _currentHitIndex.Value;
                return;
            }

            Debug.Assert(_mainWindow.Finder.HitCount > 0);
            if (_mainWindow.Finder.HitCount == 1)
            {
                previous = 1;
                next = 1;
                return;
            }

            previous = -1;
            int? currentIndex = this.CurrentIndex;
            if (currentIndex.HasValue)
            {
                Debug.Assert(_mainWindow.Finder.HitCount == _mainWindow.Finder.Hits.Count);
                for (int ii = 0; ii < _mainWindow.Finder.Hits.Count; ii++)
                {
                    JsonObject foo = _mainWindow.Finder.Hits[ii];
                    if (foo.OverallIndex <= currentIndex.Value)
                    {
                        previous = ii;
                    }
                }
            }

            previous = (previous + _mainWindow.Finder.HitCount) % _mainWindow.Finder.HitCount;
            next = (previous + 1 + _mainWindow.Finder.HitCount) % _mainWindow.Finder.HitCount;

            Debug.Assert(previous >= 0);
            Debug.Assert(previous < _mainWindow.Finder.HitCount);

            Debug.Assert(next >= 0);
            Debug.Assert(next < _mainWindow.Finder.HitCount);
        }
    }
}
