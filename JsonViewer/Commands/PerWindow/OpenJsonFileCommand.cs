namespace JsonViewer.Commands.PerWindow
{
    using System.Threading.Tasks;
    using System.Windows.Input;
    using JsonViewer.View;
    using Microsoft.Win32;

    public class OpenJsonFileCommand : BaseCommand
    {
        private static string _lastFile = string.Empty;

        public OpenJsonFileCommand(MainWindow mainWindow)
            : base("Open Json file", true)
        {
            this.MainWindow = mainWindow;

            this.AddKeyGesture(new KeyGesture(Key.O, ModifierKeys.Control));
        }

        public static string LastFile { get => _lastFile; set => _lastFile = value; }

        public static string PickJsonFile(MainWindow mainWindow, string title, string initialDirectory)
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

        public static async Task LoadFile(string filePath, MainWindow mainWindow, object parameter)
        {
            MainWindow.DisplayMode displayMode = MainWindow.DisplayMode.TreeView;
            if (await mainWindow.SetText(System.IO.File.ReadAllText(filePath)))
            {
                _lastFile = filePath;
            }
            else
            {
                displayMode = MainWindow.DisplayMode.RawText;
            }

            new SwitchModeCommand(mainWindow, string.Empty, displayMode).Execute(parameter);
        }

        public override void Execute(object parameter)
        {
            string filePath = OpenJsonFileCommand.PickJsonFile(this.MainWindow, "Pick Json content file", GetInitialDirectory());
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    _ = OpenJsonFileCommand.LoadFile(filePath, this.MainWindow, parameter);
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
