﻿namespace JsonViewer.View
{
    using System.ComponentModel;
    using System.Deployment.Application;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;
    using JsonViewer.Model;
    using Utilities;

    /// <summary>
    /// Interaction logic for StatusBar.xaml
    /// </summary>
    public partial class StatusBar : UserControl, INotifyPropertyChanged
    {
        private MainWindow _mainWindow = null;
        private RootObject _rootObject = null;

        public StatusBar()
        {
            InitializeComponent();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string CurrentIndex
        {
            get
            {
                string currentIndexString = "--";
                int? currentIndex = _mainWindow?.Tree?.SelectedIndex;
                if (currentIndex.HasValue)
                {
                    currentIndexString = (currentIndex.Value + 1).ToString();
                }

                return currentIndexString + " / " + (_mainWindow?.RootObject?.TotalChildCount.ToString() ?? "--");
            }
        }

        public string CurrentPath
        {
            get
            {
                return (_mainWindow?.Tree?.SelectedItem as TreeViewData)?.JsonObject.Path;
            }
        }

        public int SimilarCount
        {
            get
            {
                return _mainWindow?.SimilarHighlighter?.MatchCount ?? 0;
            }
        }

        public Visibility SimilarCountVisibility
        {
            get
            {
                return this.SimilarCount > 1 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public string CurrentVersion
        {
            get
            {
                string result = string.Empty;
                try
                {
                    result = ApplicationDeployment.CurrentDeployment?.CurrentVersion?.ToString();
                }
                catch (InvalidDeploymentException)
                {
                    result = "local";
                }
                catch
                {
                }

                if (string.IsNullOrEmpty(result))
                {
                    result = "unknown";
                }

                return result;
            }
        }

        public Visibility FlavorVisibility
        {
#if DEBUG
            get => Visibility.Visible;
#else
            get => Visibility.Collapsed;
#endif
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _mainWindow = (MainWindow)this.DataContext;
            FileLogger.Assert(_mainWindow != null);

            _mainWindow.Tree.PropertyChanged += OnTreePropertyChanged;
            _mainWindow.SimilarHighlighter.PropertyChanged += OnSimilarHighlighterPropertyChanged;
            _mainWindow.PropertyChanged += OnMainWindowPropertyChanged;
            this.AddRootObjectPropertyChangedHandler();
        }

        private void AddRootObjectPropertyChangedHandler()
        {
            if (_rootObject == _mainWindow.RootObject)
            {
                return;
            }

            if (_rootObject != null)
            {
                _rootObject.PropertyChanged -= OnRootObjectPropertyChanged;
            }

            _rootObject = _mainWindow.RootObject;
            if (_rootObject != null)
            {
                _rootObject.PropertyChanged += OnRootObjectPropertyChanged;
            }

            NotifyPropertyChanged.FirePropertyChanged("CurrentIndex", this, this.PropertyChanged);
        }

        private void OnMainWindowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "RootObject":
                    AddRootObjectPropertyChangedHandler();
                    break;
            }
        }

        private void OnRootObjectPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "TotalChildCount":
                    NotifyPropertyChanged.FirePropertyChanged("CurrentIndex", this, this.PropertyChanged);
                    break;
            }
        }

        private void OnTreePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "SelectedIndex":
                    NotifyPropertyChanged.FirePropertyChanged(new string[] { "CurrentIndex", "CurrentPath" }, this, this.PropertyChanged);
                    break;
            }
        }

        private void OnSimilarHighlighterPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "MatchCount":
                    NotifyPropertyChanged.FirePropertyChanged(new string[] { "SimilarCountVisibility", "SimilarCount" }, this, this.PropertyChanged);
                    break;
            }
        }
    }
}
