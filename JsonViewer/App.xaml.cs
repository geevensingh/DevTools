namespace JsonViewer
{
    using System;
    using System.Collections.Generic;
    using System.Deployment.Application;
    using System.IO;
    using System.Windows;
    using JsonViewer.Model;
    using JsonViewer.View;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private string _initialText = string.Empty;

        public static new App Current { get => (App)Application.Current; }

        public string InitialText { get => _initialText; set => _initialText = value; }

        public void CheckForUpdates()
        {
            if (!ApplicationDeployment.IsNetworkDeployed)
            {
                MessageBox.Show("This application was not network deployed.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ApplicationDeployment ad = ApplicationDeployment.CurrentDeployment;

            UpdateCheckInfo info;
            try
            {
                info = ad.CheckForDetailedUpdate();
            }
            catch (DeploymentDownloadException dde)
            {
                ErrorMessage.Show("The new version of the application cannot be downloaded at this time. \n\nPlease check your network connection, or try again later. Error: " + dde.Message);
                return;
            }
            catch (InvalidDeploymentException ide)
            {
                ErrorMessage.Show("Cannot check for a new version of the application. The ClickOnce deployment is corrupt. Please redeploy the application and try again. Error: " + ide.Message);
                return;
            }
            catch (InvalidOperationException ioe)
            {
                ErrorMessage.Show("This application cannot be updated. It is likely not a ClickOnce application. Error: " + ioe.Message);
                return;
            }

            if (!info.UpdateAvailable)
            {
                MessageBox.Show("This application is up to date.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool optional = !info.IsUpdateRequired;
            bool doUpdate = true;
            if (optional)
            {
                MessageBoxResult dr = MessageBox.Show("An update is available. Would you like to update the application now?", "Update Available", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (dr != MessageBoxResult.OK)
                {
                    doUpdate = false;
                }
            }
            else
            {
                // Display a message that the app MUST reboot. Display the minimum required version.
                MessageBox.Show(
                    "This application has detected a mandatory update from your current " +
                    "version to version " + info.MinimumRequiredVersion.ToString() +
                    ". The application will now install the update and restart.",
                    "Update",
                    button: MessageBoxButton.OK,
                    icon: MessageBoxImage.Information);
            }

            if (doUpdate)
            {
                try
                {
                    ad.Update();
                }
                catch (DeploymentDownloadException dde)
                {
                    ErrorMessage.Show("Cannot install the latest version of the application. \n\nPlease check your network connection, or try again later. Error: " + dde);
                    return;
                }

                MessageBoxResult dr = MessageBoxResult.Yes;
                if (optional)
                {
                    dr = MessageBox.Show("The application has been upgraded.  Restart now?", "Update", MessageBoxButton.YesNo, MessageBoxImage.Question);
                }

                if (dr == MessageBoxResult.Yes)
                {
                    System.Windows.Forms.Application.Restart();
                    Application.Current.Shutdown();
                }
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            FileLogger.Log("---------- Launched new instance ----------");

            List<string> args = new List<string>(e?.Args);

            FileLogger.Log("arg-count : " + args.Count);
            foreach (string arg in args)
            {
                FileLogger.Log("arg : " + arg);
            }

            if (args.Count == 1)
            {
                string launchFilePath = args[0];
                try
                {
                    _initialText = File.ReadAllText(launchFilePath);
                    Commands.PerWindow.OpenJsonFileCommand.LastFile = launchFilePath;
                }
                catch
                {
                    ErrorMessage.Show("Unable to load the following path: \r\n" + launchFilePath);
                }
            }
        }
    }
}
