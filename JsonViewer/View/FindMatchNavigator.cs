namespace JsonViewer.View
{
    using System.Diagnostics;
    using System.Windows;
    using JsonViewer.Model;
    using Utilities;

    public class FindMatchNavigator : NotifyPropertyChanged
    {
        private TabContent _tab;

        private int? _currentHitIndex = null;

        private string _findMatchText = string.Empty;

        public FindMatchNavigator(TabContent tab)
        {
            _tab = tab;

            _tab.Tree.PropertyChanged += OnTreePropertyChanged;
            _tab.Finder.PropertyChanged += OnFinderPropertyChanged;

            UpdateFindMatches();
        }

        public enum Direction
        {
            Forward,
            Backward
        }

        public Visibility ShowFindControls { get => string.IsNullOrEmpty(_findMatchText) ? Visibility.Collapsed : Visibility.Visible; }

        public string FindMatchText { get => _findMatchText; }

        private int? CurrentIndex { get => _tab.Tree.SelectedIndex; }

        public void Go(Direction direction)
        {
            FileLogger.Assert(_tab.Finder.HitCount > 0);
            GetHitIndexRange(out int previous, out int next);

            int adjusted = direction == Direction.Forward ? (previous + 1) : (next - 1);
            _currentHitIndex = (adjusted + _tab.Finder.Hits.Count) % _tab.Finder.Hits.Count;
            JsonObject hit = _tab.Finder.Hits[_currentHitIndex.Value];
            _tab.Tree.SelectItem(hit.ViewObject);
        }

        private void OnTreePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            FileLogger.Assert(sender == _tab.Tree);
            switch (e.PropertyName)
            {
                case "SelectedIndex":
                    TreeViewData newSelectedData = (TreeViewData)_tab.Tree.SelectedItem;
                    if (newSelectedData != null && _tab.Finder.Hits.Contains(newSelectedData.JsonObject))
                    {
                        _currentHitIndex = _tab.Finder.Hits.IndexOf(newSelectedData.JsonObject);
                    }

                    UpdateFindMatches();
                    break;
            }
        }

        private void OnFinderPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            FileLogger.Assert(sender == _tab.Finder);
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

            Finder finder = _tab.Finder;
            if (finder.HitCount == 0)
            {
                FileLogger.Assert(!_currentHitIndex.HasValue);
                this.SetValue(ref _findMatchText, string.Empty, new string[] { "ShowFindControls", "FindMatchText" });
                return;
            }

            int? currentIndex = this.CurrentIndex;
            if (currentIndex.HasValue)
            {
                JsonObject currentObj = _tab.RootObject.AllChildren[currentIndex.Value];
                if (finder.Hits.Contains(currentObj))
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

            FileLogger.Assert(_tab.Finder.HitCount > 0);
            if (_tab.Finder.HitCount == 1)
            {
                previous = 1;
                next = 1;
                return;
            }

            previous = -1;
            int? currentIndex = this.CurrentIndex;
            if (currentIndex.HasValue)
            {
                FileLogger.Assert(_tab.Finder.HitCount == _tab.Finder.Hits.Count);
                for (int ii = 0; ii < _tab.Finder.Hits.Count; ii++)
                {
                    JsonObject foo = _tab.Finder.Hits[ii];
                    if (foo.OverallIndex <= currentIndex.Value)
                    {
                        previous = ii;
                    }
                }
            }

            previous = (previous + _tab.Finder.HitCount) % _tab.Finder.HitCount;
            next = (previous + 1 + _tab.Finder.HitCount) % _tab.Finder.HitCount;

            FileLogger.Assert(previous >= 0);
            FileLogger.Assert(previous < _tab.Finder.HitCount);

            FileLogger.Assert(next >= 0);
            FileLogger.Assert(next < _tab.Finder.HitCount);
        }
    }
}
