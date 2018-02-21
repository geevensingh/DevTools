namespace JsonViewer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Windows;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private List<string> _args = null;
        private string _initialText = string.Empty;

        public IList<string> Args { get => _args; }

        public string InitialText { get => _initialText; set => _initialText = value; }

        internal static new App Current { get => (App)Application.Current; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _args = new List<string>(e?.Args);

            if (_args.Count > 0)
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
