using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Diagnostics;

namespace Hack
{
    class Key
    {
        public string Name = string.Empty;
        public string File = string.Empty;
        public string Type = string.Empty;

        public static Key Create(XmlReader reader, string filePath)
        {
            string nodeName = reader.Name;
            Debug.Assert(!string.IsNullOrEmpty(nodeName));
            if (nodeName == "ResourceDictionary")
            {
                return null;
            }

            string keyName = reader.GetAttribute("x:Key");
            if (string.IsNullOrEmpty(keyName))
            {
                keyName = reader.GetAttribute("x:Name");
                if (string.IsNullOrEmpty(keyName))
                {
                    return null;
                }
            }

            Key key = new Key();
            key.File = filePath;
            key.Name = keyName;
            key.Type = nodeName;
            return key;
        }

        public static Key[] CreateKnownKeys()
        {
            string[] knownResourceNames = new string[] {
                "SystemAccentColor",
                "SystemAccentColorDark1",
                "SystemAccentColorDark2",
                "SystemAccentColorDark3",
                "SystemAccentColorLight1",
                "SystemAccentColorLight2",
                "SystemAccentColorLight3",
                "SystemAltHighColor",
                "SystemAltLowColor",
                "SystemAltMediumColor",
                "SystemAltMediumHighColor",
                "SystemAltMediumLowColor",
                "SystemBaseHighColor",
                "SystemBaseLowColor",
                "SystemBaseMediumColor",
                "SystemBaseMediumHighColor",
                "SystemBaseMediumLowColor",
                "SystemChromeAltLowColor",
                "SystemChromeBlackHighColor",
                "SystemChromeBlackLowColor",
                "SystemChromeBlackMediumColor",
                "SystemChromeBlackMediumLowColor",
                "SystemChromeDisabledHighColor",
                "SystemChromeDisabledLowColor",
                "SystemChromeHighColor",
                "SystemChromeLowColor",
                "SystemChromeMediumColor",
                "SystemChromeMediumLowColor",
                "SystemChromeWhiteColor",
                "SystemColorButtonFaceColor",
                "SystemColorButtonTextColor",
                "SystemColorGrayTextColor",
                "SystemColorHighlightColor",
                "SystemColorHighlightTextColor",
                "SystemColorHotlightColor",
                "SystemColorWindowColor",
                "SystemColorWindowTextColor",
                "SystemListLowColor",
                "SystemListMediumColor",
            };

            List<Key> keys = new List<Key>();
            foreach (string knownResource in knownResourceNames)
            {
                Key key = new Key();
                key.File = @"-- Known Xaml Resource --";
                key.Name = knownResource;
                key.Type = "Color";
                keys.Add(key);
            }
            return keys.ToArray();
        }
    }
}
