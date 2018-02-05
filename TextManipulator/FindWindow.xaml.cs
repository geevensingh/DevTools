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
    public partial class FindWindow : System.Windows.Controls.Primitives.Popup
    {
        private string _text = string.Empty;
        
        public string Text { get => _text; }
        public bool ShouldSearchKeys { get => this.searchKeysCheckbox.IsChecked.Value; }
        public bool ShouldSearchValues { get => this.searchValuesCheckbox.IsChecked.Value; }
        public bool ShouldIgnoreCase { get => this.ignoreCaseCheckbox.IsChecked.Value; }

        public FindWindow()
        {
            InitializeComponent();
        }

        private void OnOpened(object sender, EventArgs e)
        {
            this.textBox.Focus();
        }

        private void OnTextBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            string oldText = _text;
            _text = this.textBox.Text;
            if (this.FindTextChanged != null)
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
            if (this.FindOptionsChanged != null)
            {
                this.FindOptionsChanged();
            }
        }
    }
}
