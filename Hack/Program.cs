using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Hack
{
    class Program
    {
        static void Main(string[] args)
        {
            List<string> xamlFiles = new List<string>(GetAllFiles(new string[] { "xaml" }));
            xamlFiles.Add(Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Windows Kits\10\DesignTime\CommonConfiguration\Neutral\UAP\10.0.15063.0\Generic\generic.xaml"));
            xamlFiles.Add(Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Windows Kits\10\DesignTime\CommonConfiguration\Neutral\UAP\10.0.15063.0\Generic\themeresources.xaml"));
            xamlFiles.Add(@"C:\Users\geevens\Downloads\RevealBrush_rs3_themeresources.xaml");
            xamlFiles.Add(@"C:\Users\geevens\Downloads\RevealBrush_rs2_themeresources.xaml");
            xamlFiles.Add(@"C:\Users\geevens\Downloads\RevealBrush_rs1_themeresources.xaml");
            xamlFiles.Add(@"C:\Users\geevens\Downloads\AcrylicBrush_rs2_themeresources.xaml");
            xamlFiles.Add(@"C:\Users\geevens\Downloads\AcrylicBrush_rs1_themeresources.xaml");

            List <Key> keys = new List<Key>();
            keys.AddRange(Key.CreateKnownKeys());

            List<ResourceUsage> usages = new List<ResourceUsage>();
            foreach (string file in xamlFiles)
            {
                XmlReaderSettings readerSettings = new XmlReaderSettings();
                readerSettings.Async = false;
                readerSettings.CloseInput = true;
                readerSettings.IgnoreComments = true;
                XmlReader reader = XmlReader.Create(file, readerSettings);


                while (reader.Read())
                {
                    switch (reader.NodeType)
                    {
                        case XmlNodeType.Element:
                            Key key = Key.Create(reader, file);
                            if (key != null)
                            {
                                keys.Add(key);
                                break;
                            }

                            ResourceUsage[] nodeUsages = ResourceUsage.Create(reader, file);
                            if (nodeUsages != null)
                            {
                                usages.AddRange(nodeUsages);
                            }
                            break;
                        case XmlNodeType.EndElement:
                            break;
                    }
                }
                reader.Close();

            }

#if false
            Debug.WriteLine(@"--------------------------------------------------------");
            Debug.WriteLine(@"-    Keys                                              -");
            Debug.WriteLine(@"--------------------------------------------------------");
            foreach (Key key in keys)
            {
                Debug.WriteLine(key.Name + " ( " + key.Type + " )");
            }

            foreach (string resourceType in new string[] { "StaticResource", "ThemeResource", "CustomResource" })
            {
                Debug.WriteLine(@"--------------------------------------------------------");
                Debug.WriteLine(@"-    " + resourceType);
                Debug.WriteLine(@"--------------------------------------------------------");
                foreach (ResourceUsage usage in usages)
                {
                    if (usage.ResourceType == resourceType)
                    {
                        Debug.WriteLine(usage.ResourceName + " ( " + usage.NodeName + " )");
                    }
                }
            }
#endif

            Debug.WriteLine(@"--------------------------------------------------------");
            Debug.WriteLine(@"-    Invalid custom resource references                -");
            Debug.WriteLine(@"--------------------------------------------------------");
            List<string> stringIds = new List<string>(GetStringIds());
            foreach (ResourceUsage usage in usages)
            {
                if (usage.ResourceType == "CustomResource")
                {
                    if (!stringIds.Contains(usage.ResourceName))
                    {
                        Debug.WriteLine(usage.ResourceName + " ( " + usage.NodeName + " )");
                    }
                }
            }

            Debug.WriteLine(@"--------------------------------------------------------");
            Debug.WriteLine(@"-    Invalid static/theme resource references          -");
            Debug.WriteLine(@"--------------------------------------------------------");
            foreach (ResourceUsage usage in usages)
            {
                if (usage.ResourceType == "StaticResource" || usage.ResourceType == "ThemeResource")
                {
                    if (FindKeyByName(usage.ResourceName, keys) == null)
                    {
                        Debug.WriteLine(usage.ResourceType + " : " + usage.ResourceName + " ( " + usage.NodeName + " - " + usage.File + " )");
                    }
                }
            }
        }

        static Key FindKeyByName(string name, List<Key> keys)
        {
            foreach (Key key in keys)
            {
                if (key.Name == name)
                {
                    return key;
                }
            }
            return null;
        }

        static string[] GetAllFiles(string[] extensions)
        {
            List<string> allFiles = new List<string>();
            foreach (string ext in extensions)
            {
                allFiles.AddRange(Directory.EnumerateFiles(@"S:\Repos\media.app\src\zune\client", @"*." + ext, SearchOption.AllDirectories));
            }
            return allFiles.ToArray();
        }

        static string[] GetStringIds()
        {
            List<string> stringIds = new List<string>();
            foreach (string filePath in GetAllFiles(new string[] { "resw" }))
            {
                // Ignore the error strings.
                if (filePath.ToLower().Contains(@"\resw\errorstrings\resources.resw"))
                {
                    continue;
                }

                // Let's only search for strings in a single language - maybe English.
                if (!filePath.ToLower().Contains(@"\en-us\"))
                {
                    continue;
                }

                XmlDocument doc = new XmlDocument();
                doc.Load(filePath);

                Debug.Assert(doc.SelectSingleNode(@"root/resheader[@name='resmimetype']/value").InnerText == "text/microsoft-resx");
                Debug.Assert(doc.SelectSingleNode(@"root/resheader[@name='version']/value").InnerText == "2.0");
                Debug.Assert(doc.SelectSingleNode(@"root/resheader[@name='reader']/value").InnerText == "System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
                Debug.Assert(doc.SelectSingleNode(@"root/resheader[@name='writer']/value").InnerText == "System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");

                XmlNodeList dataNodes = doc.SelectNodes(@"root/data");
                foreach (XmlNode dataNode in dataNodes)
                {
                    stringIds.Add(dataNode.Attributes["name"].Value);
                }
            }
            return stringIds.ToArray();
        }
    }
}
