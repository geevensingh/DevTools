namespace JsonViewer.Commands.PerWindow
{
    using JsonViewer.View;

    public class SwitchModeCommand : BaseCommand
    {
        public SwitchModeCommand(TabContent tab, string text, TabContent.DisplayMode displayMode)
            : base(text)
        {
            this.DisplayMode = displayMode;
            this.Tab = tab;
            this.Update();
        }

        private TabContent.DisplayMode DisplayMode { get; set; }

        public override void Execute(object parameter)
        {
            this.Tab.SetDisplayMode(this.DisplayMode);
        }

        protected override void OnTabPropertyChanged(string propertyName)
        {
            switch (propertyName)
            {
                case "Mode":
                    this.Update();
                    break;
            }
        }

        private void Update()
        {
            this.SetCanExecute(this.Tab.Mode != this.DisplayMode);
        }
    }
}
