﻿namespace JsonViewer.Commands.PerWindow
{
    using System.Diagnostics;
    using System.Windows.Input;
    using JsonViewer.Model;
    using JsonViewer.View;

    public class FindNextCommand : BaseCommand
    {
        public FindNextCommand(MainWindow mainWindow)
            : base("Next", true)
        {
            this.MainWindow = mainWindow;
            this.MainWindow.Finder.PropertyChanged += OnFinderPropertyChanged;
            this.Update();

            this.AddKeyGesture(new KeyGesture(Key.Right, ModifierKeys.Control));
            this.AddKeyGesture(new KeyGesture(Key.Right, ModifierKeys.Alt));
        }

        public override void Execute(object parameter)
        {
            this.MainWindow.Toolbar.FindMatchNavigator.Go(FindMatchNavigator.Direction.Forward);
        }

        private void OnFinderPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            FileLogger.Assert(sender == this.MainWindow.Finder);
            switch (e.PropertyName)
            {
                case "HitCount":
                    this.Update();
                    break;
            }
        }

        private void Update()
        {
            this.SetCanExecute(this.MainWindow.Finder.HitCount > 0);
        }
    }
}
