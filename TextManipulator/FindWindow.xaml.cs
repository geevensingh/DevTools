using System;
using System.Collections.Generic;
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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace TextManipulator
{
    /// <summary>
    /// Interaction logic for FindWindow.xaml
    /// </summary>
    public partial class FindWindow : Window
    {
        private Finder _finder;
        private bool _allowEvents = false;

        private string _text = string.Empty;
        
        public string Text { get => _text; }
        public bool ShouldSearchKeys { get => this.searchKeysCheckbox.IsChecked.Value; }
        public bool ShouldSearchValues { get => this.searchValuesCheckbox.IsChecked.Value; }
        public bool ShouldSearchParentValues { get => this.searchParentValuesCheckbox.IsChecked.Value; }
        public bool ShouldIgnoreCase { get => this.ignoreCaseCheckbox.IsChecked.Value; }

        internal FindWindow(Window owner, Finder finder)
        {
            InitializeComponent();
            this.Owner = owner;
            _finder = finder;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            this.textBox.Text = _finder.Text;
            this.ignoreCaseCheckbox.IsChecked = _finder.ShouldIgnoreCase;
            this.searchKeysCheckbox.IsChecked = _finder.ShouldSearchKeys;
            this.searchValuesCheckbox.IsChecked = _finder.ShouldSearchValues;
            this.searchParentValuesCheckbox.IsChecked = _finder.ShouldSearchParentValues;
            this.textBox.Focus();
            _allowEvents = true;
        }

        private void OnTextBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            string oldText = _text;
            _text = this.textBox.Text;
            if (_allowEvents && this.FindTextChanged != null)
            {
                this.FindTextChanged(oldText, _text);
            }
        }

        public delegate void FindTextChangedEventHandler(string oldText, string newText);
        public event FindTextChangedEventHandler FindTextChanged;

        public delegate void FindOptionsChangedEventHandler();
        public event FindOptionsChangedEventHandler FindOptionsChanged;

        private void OnChecked(object sender, RoutedEventArgs e)
        {
            if (_allowEvents && this.FindOptionsChanged != null)
            {
                this.FindOptionsChanged();
            }
        }

        private void Tree_CommandBinding_HideFind(object sender, ExecutedRoutedEventArgs e)
        {
            CommandFactory.HideFind_Execute(_finder);
        }
    }
}
