namespace JsonViewer.Commands.PerWindow
{
    using System.Windows.Input;
    using JsonViewer.View;
    using Microsoft.Win32;

    public class OpenJsonFileCommand : BaseCommand
    {
        private static string _lastFile = string.Empty;

        public OpenJsonFileCommand(TabContent mainWindow)
            : base("Open Json file", true)
        {
            this.Tab = mainWindow;

            this.AddKeyGesture(new KeyGesture(Key.O, ModifierKeys.Control));
        }

        public static string LastFile { get => _lastFile; set => _lastFile = value; }

        public static string PickJsonFile(TabContent mainWindow, string title, string initialDirectory)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = title,
                Filter = "Json files (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true,
                InitialDirectory = initialDirectory
            };
            bool? ofdResult = openFileDialog.ShowDialog(mainWindow);
            if (ofdResult.HasValue && ofdResult.Value)
            {
                return openFileDialog.FileName;
            }

            return null;
        }

        public override void Execute(object parameter)
        {
            string filePath = OpenJsonFileCommand.PickJsonFile(this.Tab, "Pick Json content file", GetInitialDirectory());
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    this.Tab.Raw_TextBox.Text = System.IO.File.ReadAllText(filePath);
                    _lastFile = filePath;
                    new SwitchModeCommand(this.Tab, string.Empty, TabContent.DisplayMode.TreeView).Execute(parameter);
                }
                catch
                {
                }
            }
        }

        private static string GetInitialDirectory()
        {
            if (string.IsNullOrEmpty(_lastFile))
            {
                return string.Empty;
            }

            return System.IO.Path.GetDirectoryName(_lastFile);
        }
    }
}
