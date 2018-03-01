namespace JsonViewer.Commands.PerWindow
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Input;
    using Microsoft.Win32;

    public class OpenJsonFileCommand : BaseCommand
    {
        private MainWindow _mainWindow = null;

        public OpenJsonFileCommand(MainWindow mainWindow)
            : base("Open Json File", true)
        {
            _mainWindow = mainWindow;

            this.AddKeyGesture(new KeyGesture(Key.O, ModifierKeys.Control));
        }

        public static string PickJsonFile(MainWindow mainWindow, string title)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = title,
                Filter = "Json files (*.json)|*.json|All files (*.*)|*.*"
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
            string filePath = OpenJsonFileCommand.PickJsonFile(_mainWindow, "Pick Json content file");
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    _mainWindow.Raw_TextBox.Text = System.IO.File.ReadAllText(filePath);
                }
                catch
                {
                }
            }
        }
    }
}
