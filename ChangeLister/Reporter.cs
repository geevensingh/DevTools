using System;
using System.Collections.Generic;
using System.Diagnostics;
using Utilities;

namespace ChangeLister
{
    class Reporter
    {
        Change[] _changes;
        Dictionary<string, int> _fileCount;
        Dictionary<Client, Dictionary<Client, List<Change>>> _org = new Dictionary<Client, Dictionary<Client, List<Change>>>();

        public Reporter(Change[] changes)
        {
            _changes = changes;
            generateFileCount();
            foreach (Change change in _changes)
            {
                Client root = change.Client.Root;
                if (!_org.ContainsKey(root))
                {
                    _org.Add(root, new Dictionary<Client, List<Change>>());
                }
                if (!_org[root].ContainsKey(change.Client))
                {
                    _org[root].Add(change.Client, new List<Change>());
                }
                _org[root][change.Client].Add(change);
            }
        }

        public void GenerateTextReport(string fileName)
        {
            Client lastClient = null;
            List<string> report = new List<string>();
            for (int ii = 0; ii < _changes.Length; ii++)
            {
                Change change = _changes[ii];
                if (change.Client != lastClient)
                {
                    lastClient = change.Client;
                    report.Add("Client: " + lastClient.DisplayName);
                    report.Add("");

                    string[] strings = lastClient.getResolveIssues();
                    if (strings.Length > 0)
                    {
                        report.Add("Resolve issues:");
                        report.AddRange(strings);
                        report.Add("");
                    }

                    strings = BuildErrorReporter.generateErrorReport(lastClient);
                    if (strings.Length > 0)
                    {
                        report.AddRange(strings);
                        report.Add("");
                    }

                    strings = lastClient.getPossibleConflicts();
                    if (strings.Length > 0)
                    {
                        report.Add("Possible conflicts:");
                        report.AddRange(strings);
                        report.Add("");
                    }
                }
                report.Add("\tChange: " + change.Number);
                //report.Add("Short Description: " + change.ShortDescription);
                report.Add("\tDescription:\r\n" + stringListToFlatTextString(change.LongDescription, "\t\t"));
                report.Add("\t\tFile List:\r\n" + stringListToFlatTextString(change.FileList, "\t\t\t"));
                report.Add("");
            }
            if (_fileCount.Keys.Count > 0)
            {
                report.Add("All files:");
            }
            foreach(string file in _fileCount.Keys)
            {
                string line = "\t" + file;
                if (_fileCount[file] > 1)
                {
                    line = line + " (" + _fileCount[file] + ")";
                }
                report.Add(line);
            }
            System.IO.File.WriteAllLines(fileName, report.ToArray());
        }

        struct FileInfo
        {
            public String FileName;
            public int HitCount;
        };

        void generateFileCount()
        {
            Dictionary<string, FileInfo> fileInfoMap = new Dictionary<string, FileInfo>();
            for (int ii = 0; ii < _changes.Length; ii++)
            {
                Change change = _changes[ii];
                for (int jj = 0; jj < change.FileList.Count; jj++)
                {
                    string file = change.FileList[jj].ToLower();
                    if (!fileInfoMap.ContainsKey(file))
                    {
                        FileInfo info;
                        info.FileName = change.FileList[jj];    // not ToLower'd
                        info.HitCount = 0;
                        fileInfoMap.Add(file, info);
                    }
                    FileInfo temp = fileInfoMap[file];
                    temp.HitCount++;
                    fileInfoMap[file] = temp;
                }
            }
            Dictionary<int, List<string>> countLookup = new Dictionary<int, List<string>>();
            int maxCount = 0;
            foreach (string lowerCaseFile in fileInfoMap.Keys)
            {
                FileInfo info = fileInfoMap[lowerCaseFile];
                int count = info.HitCount;
                maxCount = Math.Max(maxCount, count);
                if (!countLookup.ContainsKey(count))
                {
                    countLookup.Add(count, new List<string>());
                }
                countLookup[count].Add(info.FileName);
            }
            _fileCount = new Dictionary<string, int>();
            for (int ii = maxCount; ii > 0; ii--)
            {
                if (countLookup.ContainsKey(ii))
                {
                    List<string> files = countLookup[ii];
                    files.Sort();
                    foreach (string file in files)
                    {
                        _fileCount.Add(file, ii);
                    }
                }
            }
        }
        static string stringListToFlatTextString(List<string> list, string linePrefix)
        {
            if (list.Count == 0)
            {
                return "";
            }
            string result = linePrefix + list[0].Trim();
            for (int ii = 1; ii < list.Count; ii++)
            {
                result += "\r\n";
                result += linePrefix + list[ii].Trim();
            }
            return result;
        }
        static string htmlHeader = @"<!doctype html>
<html>
<head>
    <meta http-equiv=""x-ua-compatible"" content=""IE=Edge""/> 
    <meta name=""application-name"" content=""Change report""/>
    <meta name=""msapplication-tooltip"" content=""Change report for ---date---""/>
    <meta name=""msapplication-window"" content=""width=400;height=800;""/>
    <script>
        setTimeout(function () {
            window.location.reload();
        }, 1000*60*60);
    </script>
    <style>
    html {
        white-space: nowrap;
        font-family: Verdana;
        background-color: #e0e0e0;
    }
    .enlistment, .fileList {
        font-weight: normal;
    }
    .enlistment, .allFileListHeader {
        margin-left: 10px;
        font-size: 20pt;
    }
    .depot, .resolveIssueHeader, .buildIssue {
        margin-left: 30px;
        font-size: 16pt;
    }
    .shortChangeDescrip, .allFileList, .resolveIssue, .conflictHeader {
        margin-left: 50px;
        font-size: 12pt;
    }
    .longChangeDescrip, .fileList, .conflict {
        margin: 0px;
        margin-left: 70px;
        font-size: 10pt;
    }
    .canWrap {
        white-space: normal;
    }
    .error {
        color: red;
    }
    .warning {
        color: darkred;
    }
    .timeStamp {
        font-size: 10pt;
    }
    div {
        margin: 2px;
    }
    </style>
    <title>Local pending changed</title>
    </head>
    <body>";
        static string htmlFooter = "</body>\r\n</html>\r\n";

        static void addDiv(ref List<string> report, string classList, string content)
        {
            report.Add("<div class='" + classList + "'>" + content + "</div>");
        }

        public void GenerateHTMLReport(string fileName, DateTime start)
        {
            List<string> report = new List<string>();
            DateTime now = DateTime.Now;
            report.Add(htmlHeader);
            addDiv(ref report, "timeStamp", "Report generated at " + now.ToString() + " (" + Math.Ceiling((now - start).TotalSeconds) + " seconds)");

            foreach (Client root in _org.Keys)
            {
                addDiv(ref report, "enlistment", root.DisplayName);
                foreach(Client client in _org[root].Keys)
                {
                    addDiv(ref report, "depot", "Depot: " + client.DepotName);

                    string[] strings = client.getResolveIssues();
                    if (strings.Length > 0)
                    {
                        addDiv(ref report, "resolveIssueHeader error", "Resolve issues:");
                        for (int jj = 0; jj < strings.Length; jj++)
                        {
                            addDiv(ref report, "resolveIssue error", strings[jj]);
                        }
                    }

                    strings = BuildErrorReporter.generateErrorReport(client);
                    for (int jj = 0; jj < strings.Length; jj++)
                    {
                        addDiv(ref report, "buildIssue error", strings[jj]);
                    }

                    strings = client.getPossibleConflicts();
                    if (strings.Length > 0)
                    {
                        addDiv(ref report, "conflictHeader error", "Possible conflicts:");
                        for (int jj = 0; jj < strings.Length; jj++)
                        {
                            addDiv(ref report, "conflict error", strings[jj]);
                        }
                    }

                    foreach (Change change in _org[root][client])
                    {
                        addDiv(ref report, "shortChangeDescrip canWrap", change.ShortDescription);
                        if (change.LongDescription.Count > 1)
                        {
                            for (int jj = 0; jj < change.LongDescription.Count; jj++)
                            {
                                addDiv(ref report, "longChangeDescrip canWrap", change.LongDescription[jj]);
                            }
                        }
                        for (int jj = 0; jj < change.FileList.Count; jj++)
                        {
                            addDiv(ref report, "fileList", change.FileList[jj]);
                        }
                    }
                }
            }

            if (_fileCount.Keys.Count > 0)
            {
                addDiv(ref report, "allFileListHeader", "All files:");
                foreach (string file in _fileCount.Keys)
                {
                    string line = file;
                    if (_fileCount[file] > 1)
                    {
                        line += " (" + _fileCount[file] + ")";
                    }
                    addDiv(ref report, "allFileList", line);
                }
            }
            report.Add(htmlFooter);
            System.IO.File.WriteAllLines(fileName, report.ToArray());
        }

    }
}
