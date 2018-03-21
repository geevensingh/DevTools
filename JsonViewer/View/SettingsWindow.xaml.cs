namespace JsonViewer.View
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Documents;
    using System.Windows.Input;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using System.Windows.Shapes;
    using JsonViewer.Model;

    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            this.Owner = mainWindow;
        }

        public Color DefaultForegroundColor
        {
            get => Config.This.GetColor(ConfigValue.DefaultForeground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Color DefaultBackgroundColor
        {
            get => Config.This.GetColor(ConfigValue.DefaultBackground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Brush DefaultForegroundBrush
        {
            get => Config.This.GetBrush(ConfigValue.DefaultForeground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Brush DefaultBackgroundBrush
        {
            get => Config.This.GetBrush(ConfigValue.DefaultBackground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Color SelectedForegroundColor
        {
            get => Config.This.GetColor(ConfigValue.SelectedForeground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Color SelectedBackgroundColor
        {
            get => Config.This.GetColor(ConfigValue.SelectedBackground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Brush SelectedForegroundBrush
        {
            get => Config.This.GetBrush(ConfigValue.SelectedForeground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Brush SelectedBackgroundBrush
        {
            get => Config.This.GetBrush(ConfigValue.SelectedBackground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Color SearchResultForegroundColor
        {
            get => Config.This.GetColor(ConfigValue.SearchResultForeground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Color SearchResultBackgroundColor
        {
            get => Config.This.GetColor(ConfigValue.SearchResultBackground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Brush SearchResultForegroundBrush
        {
            get => Config.This.GetBrush(ConfigValue.SearchResultForeground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Brush SearchResultBackgroundBrush
        {
            get => Config.This.GetBrush(ConfigValue.SearchResultBackground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Color SimilarNodeForegroundColor
        {
            get => Config.This.GetColor(ConfigValue.SimilarNodeForeground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Color SimilarNodeBackgroundColor
        {
            get => Config.This.GetColor(ConfigValue.SimilarNodeBackground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Brush SimilarNodeForegroundBrush
        {
            get => Config.This.GetBrush(ConfigValue.SimilarNodeForeground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Brush SimilarNodeBackgroundBrush
        {
            get => Config.This.GetBrush(ConfigValue.SimilarNodeBackground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Color SelectedParentForegroundColor
        {
            get => Config.This.GetColor(ConfigValue.SelectedParentForeground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Color SelectedParentBackgroundColor
        {
            get => Config.This.GetColor(ConfigValue.SelectedParentBackground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Brush SelectedParentForegroundBrush
        {
            get => Config.This.GetBrush(ConfigValue.SelectedParentForeground);
            set
            {
                Debug.Assert(false);
            }
        }

        public Brush SelectedParentBackgroundBrush
        {
            get => Config.This.GetBrush(ConfigValue.SelectedParentBackground);
            set
            {
                Debug.Assert(false);
            }
        }
    }
}
