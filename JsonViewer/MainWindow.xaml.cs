﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace JsonViewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Finder _finder;
        Point? _initialOffset = null;

        public Point InitialOffset { set => _initialOffset = value; }

        public MainWindow()
        {
            InitializeComponent();
            var dispatcherTimer = new System.Windows.Threading.DispatcherTimer();
            dispatcherTimer.Tick += new EventHandler((object o, EventArgs ea) =>
            {
                dispatcherTimer.Stop();
                this.Raw_TextBox.Text = System.IO.File.ReadAllText(@"S:\Repos\DevTools\JsonViewer\Test.json");
            });
            dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
            dispatcherTimer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowPlacementSerializer.SetPlacement(this, Properties.Settings.Default.MainWindowPlacement, _initialOffset);
            if (_initialOffset.HasValue)
            {
                this.SaveWindowPosition();
            }
        }

        private void SaveWindowPosition()
        {
            Properties.Settings.Default.MainWindowPlacement = WindowPlacementSerializer.GetPlacement(this);
            Properties.Settings.Default.Save();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            this.SaveWindowPosition();
            base.OnClosing(e);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _finder = new Finder(this);
            Config config = Config.This;
            this.Tree.Foreground = config.GetBrush(ConfigValue.treeViewForeground);
            this.Tree.Resources[SystemColors.HighlightBrushKey] = config.GetBrush(ConfigValue.treeViewHighlightBrushKey);
            this.Tree.Resources[SystemColors.HighlightTextBrushKey] = config.GetBrush(ConfigValue.treeViewHighlightTextBrushKey);
            this.Tree.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = config.GetBrush(ConfigValue.treeViewInactiveSelectionHighlightBrushKey);
            this.Tree.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = config.GetBrush(ConfigValue.treeViewInactiveSelectionHighlightTextBrushKey);
        }

        private void SetErrorMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                this.Banner.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.Banner.Text = message;
                this.Banner.Visibility = Visibility.Visible;
            }
        }

        private void Raw_TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Debug.Assert(sender.Equals(this.Raw_TextBox));
            this.ReloadAsync();
        }

        private async void ReloadAsync()
        {
            JsonObjectFactory factory = new JsonObjectFactory();
            IList<JsonObject> jsonObjects = await factory.Parse(this.Raw_TextBox.Text);
            _finder.SetObjects(jsonObjects);
            if (jsonObjects == null)
            {
                this.SetErrorMessage("Unable to parse given string");
            }
            else
            {
                this.SetErrorMessage(string.Empty);
                this.Tree.ItemsSource = TreeViewDataFactory.CreateCollection(jsonObjects);
            }
        }

        private void ContextExpandChildren_Click(object sender, RoutedEventArgs e)
        {
            Debug.Assert(sender as FrameworkElement == sender);
            FrameworkElement element = (sender as FrameworkElement);
            Debug.Assert(element.DataContext.GetType() == typeof(TreeViewData));
            this.Tree.ExpandChildren(element.DataContext as TreeViewData);
        }

        private void ContextExpandAll_Click(object sender, RoutedEventArgs e)
        {
            Debug.Assert(sender as FrameworkElement == sender);
            FrameworkElement element = (sender as FrameworkElement);
            Debug.Assert(element.DataContext.GetType() == typeof(TreeViewData));
            this.Tree.ExpandSubtree(element.DataContext as TreeViewData);
        }

        private void ContextCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            Debug.Assert(sender as FrameworkElement == sender);
            FrameworkElement element = (sender as FrameworkElement);
            Debug.Assert(element.DataContext.GetType() == typeof(TreeViewData));
            this.Tree.CollapseSubtree(element.DataContext as TreeViewData);
        }

        private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
        {
            this.Tree.ExpandAll();
        }
        private void CollapseAllButton_Click(object sender, RoutedEventArgs e)
        {
            this.Tree.CollapseAll();
        }

        private void ContextCopyValue_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(((sender as FrameworkElement).DataContext as TreeViewData).Value);
        }
        private void ContextCopyEscapedValue_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(CSEscape.Escape(((sender as FrameworkElement).DataContext as TreeViewData).Value));
        }

        private void Tree_CommandBinding_Copy(object sender, ExecutedRoutedEventArgs e)
        {
            TreeViewData selectedData = this.Tree.SelectedItem as TreeViewData;
            if (selectedData != null)
            {
                Clipboard.SetText(selectedData.Value);
            }
        }

        private void Tree_CommandBinding_Find(object sender, ExecutedRoutedEventArgs e)
        {
            _finder.ShowWindow();
        }

        private void Tree_CommandBinding_HideFind(object sender, ExecutedRoutedEventArgs e)
        {
            CommandFactory.HideFind_Execute(_finder);
        }

        private void Reload_CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Config.Reload();
            this.ReloadAsync();
        }

        private void NewWindow_CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            MainWindow newWindow = new MainWindow();
            newWindow.InitialOffset = new Point(20, 20);
            newWindow.Show();
        }
    }
}