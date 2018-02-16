namespace JsonViewer
{
    using System.IO;
    using System.Windows;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private string[] _args = new string[] { };
        private string _initialText = string.Empty;

        public string[] Args { get => _args; }

        public string InitialText { get => _initialText; set => _initialText = value; }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            _args = e.Args;

            if (_args.Length > 0)
            {
                string launchFilePath = _args[0];
                if (File.Exists(launchFilePath))
                {
                    _initialText = File.ReadAllText(launchFilePath);
                }
            }
        }
    }
}
