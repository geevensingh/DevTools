using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Utilities
{
    public class SDOperations
    {
        string sdroot = Environment.GetEnvironmentVariable("SDXROOT");
        string sdPath = null;

        private string _user = null;
        private List<Client> _clients = null;

        public string SDRoot
        {
            get { return sdroot; }
            set { sdroot = value; }
        }

        public SDOperations()
        {
            _Init(String.Empty);
        }
        public SDOperations(string user)
        {
            _Init(user);
        }

        private void _Init(string user)
        {
            _user = user;

            if (String.IsNullOrEmpty(SDRoot))
            {
                //SDRoot = @"o:\full\winmain";
                SDRoot = @"d:\wec_main_dev";
            }
            if (System.IO.Directory.Exists(SDRoot))
            {
                SDRoot += @"\";
            }
            else
            {
                SDRoot = System.IO.Directory.GetCurrentDirectory();
            }
            sdPath = System.IO.Path.Combine(SDRoot, @"tools\x86\sd.exe");
            Debug.Assert(System.IO.File.Exists(sdPath));
        }

        public Change[] GetPendingChangeLists(Depot[] depots)
        {
            List<Change> changes = new List<Change>();
            foreach (Depot depot in depots)
            {
                Client someClient = depot.Clients[0];
                Debug.Assert(!String.IsNullOrEmpty(_user));
                ProcessHelper proc = new ProcessHelper(sdPath, @" changes -u " + _user + @" -s pending");
                proc.WorkingDirectory = someClient.Path;
                string[] lines = proc.Go();
                foreach (string line in lines)
                {
                    Client changeClient = _getClientFromChangeDescription(line, depot);
                    Change change = _processOneLineChangeDescription(line, changeClient);
                    Debug.Assert(change != null);
                    changes.Add(change);
                }
            }
            changes.Sort(new Comparison<Change>(Change.Compare));
            return changes.ToArray();
        }

        // Processes something like:
        // Change 1722271 on 2012/05/01 09:15:07 by REDMOND\geevens@GEEVENS-DEV-working.modern.mail-3 *pending* '   fix assert when  MailHeaderRe'
        Client _getClientFromChangeDescription(string line, Depot depot)
        {
            string clientName = line.Split(new char[] { '@' })[1];  // everything after an '@'
            clientName = clientName.Split(new char[] { ' ' })[0];   // everything before a ' '
            for (int ii = 0; ii < depot.Clients.Length; ii++)
            {
                if (depot.Clients[ii].Name == clientName)
                {
                    return depot.Clients[ii];
                }
            }
            Debug.Assert(false);
            return null;
        }

        // Processes something like:
        // Change 1722271 on 2012/05/01 09:15:07 by REDMOND\geevens@GEEVENS-DEV-working.modern.mail-3 *pending* '   fix assert when  MailHeaderRe'
        Change _processOneLineChangeDescription(string line, Client client)
        {
            Change change = new Change();
            string[] strings = line.Split(new char[] { ' ' });
            change.Number = strings[1];
            long number;
            Debug.Assert(Int64.TryParse(change.Number, out number));
            change.Client = client;
            change.ShortDescription = line.Split(new char[] { '\'' })[1];
            List<string> longDescription, fileList;
            _getDetailedDescription(change, out longDescription, out fileList);
            change.LongDescription = longDescription;
            change.FileList = fileList;
            return change;
        }
        void _getDetailedDescription(Change change, out List<string> description, out List<string> fileList)
        {
            ProcessHelper proc = new ProcessHelper(sdPath, @"-c " + change.Client.Name + @" change -o " + change.Number);
            proc.WorkingDirectory = change.Client.Path;
            List<string> lines = new List<string>(proc.Go());

            bool foundDescription = false;
            bool foundFiles = false;

            description = new List<string>();
            fileList = new List<string>();
            for (int ii = 0; ii < lines.Count; ii++)
            {
                string line = lines[ii];
                if (!foundDescription)
                {
                    if (line.StartsWith("Description:"))
                    {
                        foundDescription = true;
                    }
                }
                else    // foundDescription
                {
                    if (!foundFiles)
                    {
                        if (line.StartsWith("Files:"))
                        {
                            foundFiles = true;
                        }
                        else
                        {
                            description.Add(line);
                        }
                    }
                    else    // foundDescription &&  foundFiles
                    {
                        fileList.Add(line.Split(new char[] { '#' })[0].Trim());
                    }
                }
            }
            while ((description.Count > 0) && (description[description.Count - 1].Trim().Length == 0))
            {
                description.RemoveAt(description.Count - 1);
            }
            while ((fileList.Count > 0) && (fileList[fileList.Count - 1].Trim().Length == 0))
            {
                fileList.RemoveAt(fileList.Count - 1);
            }
        }

        public string[] GetAllOpenedFiles(string path, bool local = false)
        {
            ProcessHelper proc = new ProcessHelper(sdPath, @" opened " + (local ? "-l" : "-a -u " + _user));
            proc.WorkingDirectory = path;
            return proc.Go();
        }

        public Client[] GetAllClientsForUser()
        {
            if (_clients == null)
            {
                Debug.Assert(!String.IsNullOrEmpty(_user));
                ProcessHelper proc = new ProcessHelper(sdPath, @"clients -u " + _user);
                proc.WorkingDirectory = SDRoot;
                string[] lines = proc.Go();
                _clients = new List<Client>();
                // Returns strings in a form like:
                // Client GEEVENS-DEV-HP-130305104045 2013/04/15 18:34:34 root d:\cmx_green '   Created by REDMOND\geevens.    Created on 2013/03/05 10:41:27. '
                for (int ii = 0; ii < lines.Length; ii++)
                {
                    string line = lines[ii];
                    Debug.Assert(line.StartsWith("Client "));
                    Client rootClient = new Client();
                    rootClient.Name = line.Split(new char[] { ' ' })[1];
                    rootClient.Path = line.Split(new string[] { " root " }, StringSplitOptions.None)[1].Split(new char[] { '\'' })[0].Trim();
                    _clients.AddRange(GetSubClients(rootClient));
                }
                _clients.Sort(new Comparison<Client>(Client.Compare));

            }
            return _clients.ToArray();
        }

        public Client[] GetSubClients(Client root)
        {
            List<Client> subClients = new List<Client>();
            string oldSDXRoot = Environment.GetEnvironmentVariable("SDXROOT");
            if (root.IsLocal)
            {
                Environment.SetEnvironmentVariable("SDXROOT", root.Path);
                ProcessHelper proc = new ProcessHelper(SDRoot + @"\tools\sdx.cmd", "projects");
                proc.WorkingDirectory = root.Path;
                string[] lines = proc.Go();
                Environment.SetEnvironmentVariable("SDXROOT", oldSDXRoot);
                for (int ii = 0; ii < lines.Length; ii++)
                {
                    if (lines[ii].StartsWith("----------------"))
                    {
                        Client subClient = new Client(root);
                        subClient.Name = root.Name;
                        subClient.Path = lines[ii + 2].Trim();
                        Debug.Assert(System.IO.Directory.Exists(subClient.Path));
                        subClients.Add(subClient);
                    }
                }
            }
            else
            {
                subClients.Add(root);
            }
            return subClients.ToArray();
        }

        public string[] GetResolveIssues(Client client)
        {
            List<string> files = new List<string>();
            if (client.IsLocal)
            {
                ProcessHelper proc = new ProcessHelper(sdPath, @"-c " + client.Name + " resolve -n -am");
                proc.WorkingDirectory = client.Path;
                string[] lines = proc.Go();

                // Returns strings in a form like:
                // d:\Source\w.m.mail-6\modern\mail\components\dirs.sln - merging *undo* //depot/working/modern/mail/modern/mail/components/dirs.sln#17
                for (int ii = 0; ii < lines.Length; ii++)
                {
                    string file = lines[ii].Split(new string[] { " - " }, StringSplitOptions.None)[0];
                    files.Add(file);
                }
            }
            return files.ToArray();
        }

        public string[] GetPossibleConflicts(Client client)
        {
            List<string> files = new List<string>();
            if (client.IsLocal)
            {
                ProcessHelper proc = new ProcessHelper(sdPath, @"-c " + client.Name + " sync -n");
                proc.WorkingDirectory = client.Path;
                string[] lines = proc.Go();

                // Returns all the files it would sync, but we're looking for something in the form of:
                // ... //depot/working/modern/mail/modern/mail/components/ReadingPane/ReadingPane.js - must resolve #104 before submitting
                // This is different from most lines
                for (int ii = 0; ii < lines.Length; ii++)
                {
                    string line = lines[ii];
                    if (line.Contains(" - must resolve ") && line.Contains(" before submitting"))
                    {
                        string file = line.Split(new string[] { " - ", "... " }, StringSplitOptions.None)[1];
                        files.Add(file);
                    }
                }
            }
            return files.ToArray();
        }

        public struct File
        {
            public string DepotPath;
            public string LocalPath;
        };
        public File[] GetFiles(string currentDir, string path)
        {
            List<File> files = new List<File>();
            ProcessHelper proc = new ProcessHelper(sdPath, @"have " + path);
            proc.WorkingDirectory = currentDir;
            string[] lines = proc.Go();
            foreach (string line in lines)
            {
                // Something like:
                // //depot/wec/main_dev/zune/client/xaml/music/UI/Controls/TrackStatusIcon.cpp#40 - d:\wec_main_dev\zune\client\xaml\music\UI\Controls\TrackStatusIcon.cpp
                File file;
                int poundIndex = line.IndexOf('#');
                Debug.Assert(poundIndex > 0);
                file.DepotPath = line.Substring(0, poundIndex - 1).Trim();

                const string localPathPrefix = " - ";
                int localPathStart = line.IndexOf(localPathPrefix, poundIndex) + localPathPrefix.Length;
                Debug.Assert(localPathStart > poundIndex + localPathPrefix.Length);
                file.LocalPath = line.Substring(localPathStart).Trim();
                if (!System.IO.File.Exists(file.LocalPath))
                {
                    OldLogger.LogLine("File not found locally: " + file.DepotPath, OldLogger.LevelValue.Warning);
                }

                files.Add(file);
            }

            return files.ToArray();
        }

        public void CheckOut(string filePath)
        {
            ProcessHelper proc = new ProcessHelper(sdPath, @"edit " + filePath);
            proc.WorkingDirectory = System.IO.Directory.GetCurrentDirectory();
            proc.Go();
        }
    }
}
