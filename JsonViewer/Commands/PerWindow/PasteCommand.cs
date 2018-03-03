namespace JsonViewer.Commands.PerWindow
{
    using System;
    using System.Diagnostics;
    using JsonViewer.Model;

    public class PasteCommand : BaseCommand
    {
        private MainWindow _mainWindow;

        public PasteCommand(MainWindow mainWindow)
            : base("Paste")
        {
            _mainWindow = mainWindow;
            _mainWindow.Raw_TextBox.TextChanged += OnRawTextBoxChanged;
            ClipboardManager.ClipboardChanged += OnClipboardChanged;
            this.Update();
        }

        public override void Execute(object parameter)
        {
            string jsonString = ClipboardManager.TryGetText();
            Debug.Assert(JsonObjectFactory.TryDeserialize(jsonString) != null);
            _mainWindow.Raw_TextBox.Text = jsonString;
            this.Update();
        }

        private void Update()
        {
            string jsonString = ClipboardManager.TryGetText();
            this.SetCanExecute(!string.IsNullOrWhiteSpace(jsonString) &&
                _mainWindow.Raw_TextBox.Text != jsonString &&
                JsonObjectFactory.TryDeserialize(jsonString) != null);
        }

        private void OnRawTextBoxChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            this.Update();
        }

        private void OnClipboardChanged(object sender, EventArgs e)
        {
            this.Update();
        }
    }
}
