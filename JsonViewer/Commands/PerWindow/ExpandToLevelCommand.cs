namespace JsonViewer.Commands.PerWindow
{
    using System.Diagnostics;
    using JsonViewer.Model;
    using JsonViewer.View;

    public class ExpandToLevelCommand : BaseCommand
    {
        private RootObject _rootObject = null;
        private int _depth;

        public ExpandToLevelCommand(MainWindow mainWindow, int depth)
            : base(depth > 0 ? "+" + depth.ToString() : "Nothing")
        {
            _depth = depth;
            this.MainWindow = mainWindow;
            this.MainWindow.Tree.PropertyChanged += OnTreePropertyChanged;
            this.SetRootObject(this.MainWindow.RootObject);
        }

        public override void Execute(object parameter)
        {
            this.MainWindow.Tree.ExpandAll(_depth);
        }

        protected override void OnMainWindowPropertyChanged(string propertyName)
        {
            switch (propertyName)
            {
                case "RootObject":
                    this.SetRootObject(this.MainWindow.RootObject);
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
            Debug.Assert(this.MainWindow.RootObject == _rootObject);
            this.SetCanExecute(!this.MainWindow.Tree.IsWaiting && this.MainWindow.RootObject != null && _rootObject.HasLevel(_depth));
        }
    }
}
