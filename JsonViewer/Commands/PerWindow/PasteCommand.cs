namespace JsonViewer.Commands.PerWindow
{
    using System;
    using System.Diagnostics;
    using System.Windows;
    using JsonViewer.Model;

    public class PasteCommand : BaseCommand
    {
        private ClipboardManager _clipboardManager;
        private MainWindow _mainWindow;

        public PasteCommand(MainWindow mainWindow)
            : base("Paste")
        {
            _mainWindow = mainWindow;
            _mainWindow.Raw_TextBox.TextChanged += OnRawTextBoxChanged;
            _clipboardManager = new ClipboardManager(mainWindow);
            _clipboardManager.ClipboardChanged += OnClipboardChanged;
            this.Update();
        }

        public override void Execute(object parameter)
        {
            string jsonString = Clipboard.GetText();
            Debug.Assert(JsonObjectFactory.TryDeserialize(jsonString) != null);
            _mainWindow.Raw_TextBox.Text = jsonString;
            this.Update();
        }

        private void Update()
        {
            string jsonString = Clipboard.GetText();
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
