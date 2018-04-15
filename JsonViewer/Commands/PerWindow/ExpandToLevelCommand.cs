namespace JsonViewer.Commands.PerWindow
{
    using System.Diagnostics;
    using JsonViewer.Model;
    using JsonViewer.View;

    public class ExpandToLevelCommand : BaseCommand
    {
        private RootObject _rootObject = null;
        private int _depth;

        public ExpandToLevelCommand(TabContent tab, int depth)
            : base(depth > 0 ? "+" + depth.ToString() : "Nothing")
        {
            _depth = depth;
            this.Tab = tab;
            this.Tab.Tree.PropertyChanged += OnTreePropertyChanged;
            this.SetRootObject(this.Tab.RootObject);
        }

        public override void Execute(object parameter)
        {
            this.Tab.Tree.ExpandAll(_depth);
        }

        protected override void OnTabPropertyChanged(string propertyName)
        {
            switch (propertyName)
            {
                case "RootObject":
                    this.SetRootObject(this.Tab.RootObject);
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
            FileLogger.Assert(this.Tab.RootObject == _rootObject);
            this.SetCanExecute(!this.Tab.Tree.IsWaiting && this.Tab.RootObject != null && _rootObject.HasLevel(_depth));
        }
    }
}
