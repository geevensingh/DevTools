﻿namespace JsonViewer.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    internal class HideFindCommand : BaseCommand
    {
        private Finder _finder = null;

        public HideFindCommand()
            : base("Hide Find Window")
        {
            App.Current.MainWindowChanged += OnMainWindowChanged;
            OnMainWindowChanged(App.Current);
        }

        public override void Execute(object parameter)
        {
            Debug.Assert(App.Current.MainWindow.Finder == _finder);
            _finder.HideWindow();
        }

        private void OnMainWindowChanged(App sender)
        {
            if (_finder != null)
            {
                _finder.PropertyChanged -= OnFinderPropertyChanged;
            }

            _finder = sender.MainWindow.Finder;
            _finder.PropertyChanged += OnFinderPropertyChanged;
            this.SetCanExecute(_finder.HasWindow);
        }

        private void OnFinderPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Debug.Assert(sender == _finder);
            if (e.PropertyName == "HasWindow")
            {
                this.SetCanExecute(_finder.HasWindow);
            }
        }
    }
}