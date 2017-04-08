using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Utilities;

namespace ChangeLister
{
    class Program
    {
        static string outputLocation = @"\\geevens-server\public\web\";
        static string textFileLocation = outputLocation + "output.txt";
        static string htmlFileLocation = outputLocation + "output.html";

        static void Main(string[] args)
        {
            DateTime start = DateTime.Now;
            SDOperations sd = new SDOperations(@"redmond\geevens");

            //CleanupTrailingWhitespace(@"d:\analog_dev_full", sd);
            //return;

            // Gets a list of pending changelists
            Client[] clients = sd.GetAllClientsForUser();
            Depot[] depots = Depot.makeDepots(clients);
            List<Change> changes = new List<Change>(sd.GetPendingChangeLists(depots));
            foreach (Depot depot in depots)
            {
                Change[] depotChanges = depot.GetRemainingChanges(sd);
                foreach (Change change in depotChanges)
                {
                    changes.Add(change);
                }
            }
            changes.Sort(new Comparison<Change>(Change.Compare));

            Reporter reporter = new Reporter(changes.ToArray());
#if DEBUG
            reporter.GenerateTextReport(textFileLocation);
#endif
            reporter.GenerateHTMLReport(htmlFileLocation, start);

#if DEBUG

            string[] actual = File.ReadAllLines(textFileLocation);
            string[] expected = File.ReadAllLines(outputLocation + "expected.txt");
            Debug.Assert(expected.Length == actual.Length);
            for (int ii = 0; ii < expected.Length; ii++)
            {
                Debug.Assert(expected[ii] == actual[ii]);
            }

            actual = File.ReadAllLines(htmlFileLocation);
            expected = File.ReadAllLines(outputLocation + "expected.html");
            Debug.Assert(expected.Length == actual.Length);
            for (int ii = 42; ii < expected.Length; ii++)
            {
                if ((actual[ii].ToLower().IndexOf("report generated at") == -1) || (expected[ii].ToLower().IndexOf("report generated at") == -1))
                {
                    Debug.Assert(expected[ii] == actual[ii]);
                }
            }
#endif
        }

        public static void CleanupTrailingWhitespace(string clientRoot, SDOperations sd)
        {
            Client rootClient = new Client();
            rootClient.Name = "whatever";
            rootClient.Path = clientRoot;
            List<Client> _clients = new List<Client>();
            //_clients.Add(rootClient);
            _clients.AddRange(sd.GetSubClients(rootClient));

            foreach (Client client in _clients)
            {
                foreach (string line in sd.GetAllOpenedFiles(client.Path, true))
                {
                    string filePath = line.Split(new char[] { '#' })[0];
                    string[] fileLines = File.ReadAllLines(filePath);
                    bool madeChange = false;
                    for (int ii = 0; ii < fileLines.Length; ii++)
                    {
                        string trimmed = fileLines[ii].TrimEnd();
                        madeChange |= (trimmed != fileLines[ii]);
                        fileLines[ii] = trimmed;
                    }
                    if (madeChange)
                    {
                        File.WriteAllLines(filePath, fileLines, Utilities.IOHelper.GetEncoding(filePath));
                    }
                }
            }
        }
    }
}
