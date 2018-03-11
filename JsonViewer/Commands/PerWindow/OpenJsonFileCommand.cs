namespace JsonViewer.Commands.PerWindow
{
    using System.Windows.Input;
    using JsonViewer.View;
    using Microsoft.Win32;

    public class OpenJsonFileCommand : BaseCommand
    {
        public OpenJsonFileCommand(MainWindow mainWindow)
            : base("Open Json file", true)
        {
            this.MainWindow = mainWindow;

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
            string filePath = OpenJsonFileCommand.PickJsonFile(this.MainWindow, "Pick Json content file");
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    this.MainWindow.Raw_TextBox.Text = System.IO.File.ReadAllText(filePath);
                }
                catch
                {
                }
            }
        }
    }
}
