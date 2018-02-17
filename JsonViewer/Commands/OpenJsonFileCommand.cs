namespace JsonViewer.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Win32;

    internal class OpenJsonFileCommand : BaseCommand
    {
        public OpenJsonFileCommand()
            : base("Open Json File", true)
        {
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
            MainWindow mainWindow = App.Current.MainWindow;
            string filePath = OpenJsonFileCommand.PickJsonFile(mainWindow, "Pick Json content file");
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    mainWindow.Raw_TextBox.Text = System.IO.File.ReadAllText(filePath);
                }
                catch
                {
                }
            }
        }
    }
}
