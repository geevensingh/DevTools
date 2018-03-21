namespace JsonViewer.Model
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows;
    using Newtonsoft.Json;

    internal enum ConfigValue
    {
        DefaultForeground,
        DefaultBackground,
        SelectedForeground,
        SelectedBackground,
        SearchResultForeground,
        SearchResultBackground,
        SimilarNodeForeground,
        SimilarNodeBackground,
        SelectedParentForeground,
        SelectedParentBackground,
    }

    internal static class Config
    {
        private static string _filePath = null;
        private static ConfigValues _configValues = null;
        private static bool _isInitialized = false;

        public static bool IsDefault
        {
            get
            {
                EnsureInitialized();
                return string.IsNullOrEmpty(_filePath);
            }
        }

        public static string FilePath
        {
            get
            {
                EnsureInitialized();
                return _filePath;
            }
        }

        public static ConfigValues Values
        {
            get
            {
                EnsureInitialized();
                return _configValues;
            }
        }

        private static void EnsureInitialized()
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            LoadConfig(Properties.Settings.Default.ConfigPath);
        }

        public static void Reload()
        {
            string filePath = Properties.Settings.Default.ConfigPath;
            if (Path.GetFileName(filePath).ToLower() == "config.json" &&
                File.Exists(Path.Combine(Path.GetDirectoryName(filePath), "JsonViewer.exe")) &&
                filePath.ToLower().Contains(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).ToLower()))
            {
                filePath = null;
            }

            if (!File.Exists(filePath))
            {
                filePath = null;
            }

            if (LoadConfig(filePath))
            {
                MessageBoxResult dr = MessageBox.Show("Unable to load your config.  Do you want to use the default?", "Default config?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (dr == MessageBoxResult.Yes)
                {
                    Properties.Settings.Default.ConfigPath = string.Empty;
                    Properties.Settings.Default.Save();
                    Reload();
                }
                else
                {
                    _configValues = new ConfigValues();
                    _filePath = string.Empty;
                }
            }
        }

        public static async Task<bool> Save(string filePath)
        {
            bool result = await _configValues.Save(filePath);
            if (result)
            {
                _filePath = filePath;
            }

            return result;
        }

        public static bool LoadConfig(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                filePath = GetDefaultConfigPath();
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("Error processing config:\r\n");

            List<string> exceptionMessages = new List<string>();
            JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
            {
                Error = new EventHandler<Newtonsoft.Json.Serialization.ErrorEventArgs>(
                (obj, args) =>
                {
                    exceptionMessages.Add(args.ErrorContext.Error.ToString());
                    args.ErrorContext.Handled = true;
                }),
                CheckAdditionalContent = true,
                MaxDepth = 100,
                MissingMemberHandling = MissingMemberHandling.Error
            };

            string errorMessage = string.Empty;
            try
            {
                ConfigValues values = JsonConvert.DeserializeObject<ConfigValues>(File.ReadAllText(filePath), jsonSerializerSettings);

                if (exceptionMessages.Count > 0)
                {
                    errorMessage = "Exceptions hit while processing config:\r\n\r\n";
                    errorMessage += string.Join("\r\n\r\n-------------------------\r\n\r\n", exceptionMessages.ToArray());
                }
                else
                {
                    _filePath = filePath;
                    _configValues = values;
                    return true;
                }
            }
            catch
            {
                if (!string.IsNullOrEmpty(filePath))
                {
                    errorMessage = "Unable to process the config file :\r\n" + filePath;
                }
            }

            Debug.Assert(!string.IsNullOrEmpty(errorMessage));
            if (!string.IsNullOrEmpty(errorMessage))
            {
                MessageBox.Show(errorMessage, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return false;
        }

        public static bool SetPath(string fileName)
        {
            if (!File.Exists(fileName))
            {
                return false;
            }

            List<string> exceptionMessages = new List<string>();
            JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
            {
                Error = new EventHandler<Newtonsoft.Json.Serialization.ErrorEventArgs>(
                (obj, args) =>
                {
                    exceptionMessages.Add(args.ErrorContext.Error.ToString());
                    args.ErrorContext.Handled = true;
                }),
                CheckAdditionalContent = true,
                MaxDepth = 100,
                MissingMemberHandling = MissingMemberHandling.Error
            };

            try
            {
                if (JsonConvert.DeserializeObject<ConfigValues>(File.ReadAllText(fileName), jsonSerializerSettings) == null)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            if (exceptionMessages.Count > 0)
            {
                return false;
            }

            string oldConfigPath = Properties.Settings.Default.ConfigPath;
            try
            {
                Properties.Settings.Default.ConfigPath = fileName;
                Reload();
                Properties.Settings.Default.Save();
                return true;
            }
            catch
            {
            }

            Properties.Settings.Default.ConfigPath = oldConfigPath;
            Properties.Settings.Default.Save();
            return false;
        }

        private static string GetDefaultConfigPath()
        {
            string appDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"JsonViewer");
            string newConfig = Path.Combine(appDataDirectory, @"Config.json");
            if (File.Exists(newConfig))
            {
                return newConfig;
            }

            try
            {
                if (File.Exists("Config.json"))
                {
                    Directory.CreateDirectory(appDataDirectory);
                    File.Copy("Config.json", newConfig);
                    return newConfig;
                }
            }
            catch
            {
            }

            return string.Empty;
        }
    }
}
