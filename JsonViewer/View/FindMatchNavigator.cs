namespace JsonViewer.View
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows;

    public class FindMatchNavigator : NotifyPropertyChanged
    {
        private MainWindow _mainWindow;

        private int? _currentHitIndex = null;

        private int? _currentIndex = null;

        private string _findMatchText = string.Empty;

        public FindMatchNavigator(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;

            _mainWindow.Tree.SelectedItemChanged += OnTreeSelectedItemChanged;
            _mainWindow.Finder.PropertyChanged += OnFinderPropertyChanged;
        }

        public Visibility ShowFindControls { get => string.IsNullOrEmpty(_findMatchText) ? Visibility.Collapsed : Visibility.Visible; }

        public string FindMatchText { get => _findMatchText; }

        public string CurrentIndex { get => _currentIndex.HasValue ? _currentIndex.ToString() : "--"; }

        private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            Debug.Assert(sender == _mainWindow.Tree);
            TreeViewData newSelectedData = (TreeViewData)e.NewValue;

            _currentHitIndex = null;
            int? newIndex = null;
            if (newSelectedData != null)
            {
                newIndex = newSelectedData.JsonObject.OverallIndex;
                if (newSelectedData.JsonObject.IsFindMatch)
                {
                    _currentHitIndex = _mainWindow.Finder.Hits.IndexOf(newSelectedData.JsonObject);
                }
            }

            this.SetValue(ref _currentIndex, newIndex, "CurrentIndex");
            UpdateFindMatches();
        }

        private void OnFinderPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Debug.Assert(sender == _mainWindow.Finder);
            switch (e.PropertyName)
            {
                case "HitCount":
                case "Hits":
                    _currentHitIndex = null;
                    UpdateFindMatches();
                    break;
            }
        }

        private void UpdateFindMatches()
        {
            Finder finder = _mainWindow.Finder;
            if (finder.HitCount == 0)
            {
                Debug.Assert(!_currentHitIndex.HasValue);
                this.SetValue(ref _findMatchText, string.Empty, new string[] { "ShowFindControls", "FindMatchText" });
                return;
            }

            if (_currentIndex.HasValue)
            {
                JsonObject currentObj = _mainWindow.RootObject.AllChildren[_currentIndex.Value];
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

        public void FindNextButton_Click(object sender, RoutedEventArgs e)
        {
            GetHitIndexRange(out int previous, out int next);

            _currentHitIndex = (previous + 1) % _mainWindow.Finder.Hits.Count;
            JsonObject hit = _mainWindow.Finder.Hits[_currentHitIndex.Value];
            _mainWindow.Tree.ExpandToItem(hit.ViewObject);
            UpdateFindMatches();
        }

        public void FindPreviousButton_Click(object sender, RoutedEventArgs e)
        {
            GetHitIndexRange(out int previous, out int next);

            _currentHitIndex = (next + _mainWindow.Finder.Hits.Count - 1) % _mainWindow.Finder.Hits.Count;
            JsonObject hit = _mainWindow.Finder.Hits[_currentHitIndex.Value];
            _mainWindow.Tree.ExpandToItem(hit.ViewObject);
            UpdateFindMatches();
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
            if (_currentIndex.HasValue)
            {
                Debug.Assert(_mainWindow.Finder.HitCount == _mainWindow.Finder.Hits.Count);
                for (int ii = 0; ii < _mainWindow.Finder.Hits.Count; ii++)
                {
                    JsonObject foo = _mainWindow.Finder.Hits[ii];
                    if (foo.OverallIndex <= _currentIndex.Value)
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
