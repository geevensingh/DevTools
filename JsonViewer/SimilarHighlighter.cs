namespace JsonViewer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Threading;
    using JsonViewer.View;
    using Utilities;

    internal class SimilarHighlighter
    {
        private SingularAction _action = null;
        private MainWindow _mainWindow;
        private RootObject _rootObject = null;

        public SimilarHighlighter(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _action = new SingularAction(_mainWindow.Dispatcher);

            _mainWindow.Tree.SelectedItemChanged += OnSelectedItemChanged;
            _mainWindow.PropertyChanged += OnMainWindowPropertyChanged;
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        private void OnSettingsPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "HighlightSimilarKeys":
                case "HighlightSimilarValues":
                    this.Update();
                    break;
            }
        }

        private void OnMainWindowPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "RootObject":
                    this.SetRootObject(_mainWindow.RootObject);
                    break;
            }
        }

        private void OnSelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            this.Update();
        }

        private void OnRootObjectPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "AllChildren":
                    this.Update();
                    break;
            }
        }

        private void SetRootObject(RootObject rootObject)
        {
            if (_rootObject == rootObject)
            {
                return;
            }

            if (_rootObject != null)
            {
                _rootObject.PropertyChanged -= OnRootObjectPropertyChanged;
            }

            _rootObject = rootObject;
            if (_rootObject != null)
            {
                _rootObject.PropertyChanged += OnRootObjectPropertyChanged;
            }

            this.Update();
        }

        private void Update()
        {
            if (_rootObject == null)
            {
                return;
            }

            JsonObject selectedObject = (_mainWindow?.Tree?.SelectedItem as TreeViewData)?.JsonObject;
            FindRule newKeyRule = null;
            FindRule newValueRule = null;

            if (selectedObject != null && Properties.Settings.Default.HighlightSimilarKeys)
            {
                newKeyRule = new FindRule(
                    text: selectedObject.Key,
                    ignoreCase: false,
                    searchKeys: true,
                    searchValues: false,
                    searchValueTypes: false,
                    appliesToParents: false,
                    exactMatch: true);
            }

            if (selectedObject != null && Properties.Settings.Default.HighlightSimilarValues)
            {
                newValueRule = new FindRule(
                    text: selectedObject.ValueString,
                    ignoreCase: false,
                    searchKeys: false,
                    searchValues: true,
                    searchValueTypes: false,
                    appliesToParents: false,
                    exactMatch: true);
            }

            Func<Guid, SingularAction, Task<bool>> updateAction = new Func<Guid, SingularAction, Task<bool>>(async (actionId, action) =>
            {
                foreach (JsonObject obj in _rootObject.AllChildren)
                {
                    obj.MatchRule = null;
                    foreach (FindRule rule in new FindRule[] { newKeyRule, newValueRule })
                    {
                        if (rule != null && rule.Matches(obj))
                        {
                            obj.MatchRule = rule;
                        }
                    }

                    if (!await action.YieldAndContinue(actionId))
                    {
                        return false;
                    }
                }

                return true;
            });

            _action.BeginInvoke(DispatcherPriority.Background, updateAction);

        }
    }
}
