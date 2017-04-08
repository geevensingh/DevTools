using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Utilities
{
    public class Depot
    {
        private string _name;
        private List<Client> _clients = new List<Client>();

        public string Name
        {
            get
            {
                return _name;
            }
        }

        public Client[] Clients
        {
            get
            {
                return _clients.ToArray();
            }
        }

        public Depot(string name, Client[] clients)
        {
            _name = name;
            _clients = new List<Client>(clients);
            _clients.Sort(new Comparison<Client>(Client.Compare));

            foreach(Client client in clients)
            {
                client.Depot = this;
            }
        }

        public Client GetClient(string name)
        {
            for (int ii = 0; ii < _clients.Count; ii++)
            {
                if (_clients[ii].Name == name)
                {
                    return _clients[ii];
                }
            }
            Debug.Assert(false);
            return null;
        }

        public Change[] GetRemainingChanges(SDOperations sd)
        {
            List<Change> changes = new List<Change>();
            List<string> lines = new List<string>(sd.GetAllOpenedFiles(_clients[0].Path));
            Dictionary<string, Change> defaultChangeByClient = new Dictionary<string, Change>();

            foreach (string line in lines)
            {
                // Parse a line something like:
                // //depot/ChangeLister/ProcessHelper.cs#1 - edit default change (text) by REDMOND\geevens@GEEVENS-DEV-HP-D-TH2-HOLOLENS
                if (line.IndexOf(" default change ") != -1)
                {
                    string clientName = line.Split(new char[] { '@' })[1];  // everything after an '@'
                    clientName = clientName.Split(new char[] { ' ' })[0];   // everything before a ' '
                    if (!defaultChangeByClient.ContainsKey(clientName))
                    {
                        Change change = new Change();
                        change.Client = GetClient(clientName);
                        change.Number = "default";
                        change.ShortDescription = "default changelist for " + change.Client.Name;
                        change.LongDescription = new List<string>();
                        change.LongDescription.Add(change.ShortDescription);
                        change.FileList = new List<string>();
                        defaultChangeByClient.Add(clientName, change);
                    }
                    defaultChangeByClient[clientName].FileList.Add(line.Split(new char[] { '#' })[0]);
                }
            }

            foreach(Change change in defaultChangeByClient.Values)
            {
                changes.Add(change);
            }

            return changes.ToArray();
        }

        static public Depot[] makeDepots(Client[] clients)
        {
            Dictionary<string, List<Client>> depots = new Dictionary<string, List<Client>>();
            foreach (Client c in clients)
            {
                if (!depots.ContainsKey(c.DepotName))
                {
                    depots[c.DepotName] = new List<Client>();
                }
                depots[c.DepotName].Add(c);
            }
            List<Depot> result = new List<Depot>();
            foreach (string name in depots.Keys)
            {
                result.Add(new Depot(name, depots[name].ToArray()));
            }
            return result.ToArray();
        }
    }
}
