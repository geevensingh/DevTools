using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;

namespace GitGui
{
    class Settings
    {
        private static Settings _current = new Settings();
        public static Settings Current
        {
            get
            {
                return Copy();
            }
        }

        public static Settings Copy()
        {
            return _current.MemberwiseClone() as Settings;
        }

        public static void Commit(Settings newSettings)
        {
            var copy = Copy();
            bool isDirty = false;
            if (string.Join("\r\n", _current._repos.ToArray()) != string.Join("\r\n", newSettings._repos.ToArray()))
            {
                copy._repos = newSettings._repos;
                isDirty = true;
            }

            if (isDirty)
            {
                var settings = Properties.Settings.Default;
                settings.repos = string.Join("\r\n", copy._repos.ToArray());
                settings.Save();

                _current = copy;
            }
        }

        public Settings()
        {
            var settings = Properties.Settings.Default;
            _repos = new List<string>(settings.repos.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries));
        }

        private List<string> _repos = new List<string>();
        public string[] Repos
        {
            get
            {
                return _repos.ToArray();
            }
            set
            {
                _repos = new List<string>(value);
            }
        }
    }
}
