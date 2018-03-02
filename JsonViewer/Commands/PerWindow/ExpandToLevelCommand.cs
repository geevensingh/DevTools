namespace JsonViewer.Commands.PerWindow
{
    using System;
    using System.Diagnostics;

    public class ExpandToLevelCommand : BaseCommand
    {
        private MainWindow _mainWindow = null;
        private RootObject _rootObject = null;
        private int _depth;

        public ExpandToLevelCommand(MainWindow mainWindow, int depth)
            : base(depth > 0 ? "+" + depth.ToString() : "Nothing")
        {
            _depth = depth;
            _mainWindow = mainWindow;
            _mainWindow.PropertyChanged += OnMainWindowPropertyChanged;
            _mainWindow.Tree.PropertyChanged += OnTreePropertyChanged;
            this.SetRootObject(_mainWindow.RootObject);
        }

        public override void Execute(object parameter)
        {
            _mainWindow.Tree.CollapseAll();
            _mainWindow.Tree.ExpandAll(_depth);
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

        private void OnTreePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "IsWaiting":
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

        private void OnRootObjectPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "AllChildren":
                    this.Update();
                    break;
            }
        }

        private void Update()
        {
            Debug.Assert(_mainWindow.RootObject == _rootObject);
            this.SetCanExecute(!_mainWindow.Tree.IsWaiting && CollapseAllCommand.HasLevel(_rootObject, _depth));
        }
    }
}
