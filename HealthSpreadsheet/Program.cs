using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HealthSpreadsheet
{
    class Program
    {
        static string rootPath = @"\\geevens-server\incoming\Health-Spreadsheet";
        static Dictionary<string, Dictionary<DateTime, Bucket>> reasonLookup = new Dictionary<string, Dictionary<DateTime, Bucket>>();
        static string[] reasonStrings;

        static void Main(string[] args)
        {
            reasonStrings = File.ReadAllLines(Path.Combine(rootPath, "Reasons.csv"));
            foreach (string reasonString in reasonStrings)
            {
                reasonLookup.Add(reasonString, new Dictionary<DateTime, Bucket>());
            }

            string[] summaryOutputPaths = Directory.EnumerateFiles(rootPath, @"AllUpBadSchedulesSummaryByReason????_??_??.ss.csv", SearchOption.TopDirectoryOnly).ToArray();
            foreach (string summaryOutputPath in summaryOutputPaths)
            {
                DateTime date = GetDateFromFilePath(summaryOutputPath);
                foreach (string reasonString in reasonStrings)
                {
                    reasonLookup[reasonString].Add(date, new Bucket(reasonString));
                }

                string[] summaryOutputLines = File.ReadAllLines(summaryOutputPath);
                for (int ii = 1; ii < summaryOutputLines.Length; ii++)
                {
                    if (string.IsNullOrEmpty(summaryOutputLines[ii]))
                    {
                        continue;
                    }

                    Bucket bucket = Bucket.CreateFromLine(date, summaryOutputLines[0], summaryOutputLines[ii]);
                    string reasonString = bucket.GetBestReason(reasonStrings);
                    if (reasonString == null)
                    {
                        Console.WriteLine("Unable to determine the best bucket for " + bucket.Reason);
                        return;
                    }

                    reasonLookup[reasonString][date].AddFromBucket(bucket);
                }
            }

            // All bucket lists should have the same length
            Debug.Assert(reasonLookup.All(x => x.Value.Count == reasonLookup.First().Value.Count));

            DateTime latestDate = GetLatestDate();
            reasonStrings = reasonStrings.OrderBy(x => reasonLookup[x], new SpecialComparer()).ToArray();

            WriteCSVByReason(x => x.Count.ToString(), "Faulted-Count.csv");
            WriteCSVByReason(x => x.InvoiceValue.ToString("F"), "Faulted-Invoice-Value.csv");
            WriteCSVByReason(x => x.ConsumerValue.ToString("F"), "Faulted-Consumer-Value.csv");
        }

        private static void WriteCSVByReason(Func<Bucket, string> func, string fileName)
        {
            StringBuilder fileContents = new StringBuilder();

            List<string> columns = new List<string>();
            columns.Add("Reason");
            foreach (DateTime date in reasonLookup[reasonStrings[0]].Keys)
            {
                columns.Add(date.ToShortDateString());
            }
            fileContents.AppendLine(string.Join(",", columns));

            foreach (string reasonString in reasonStrings)
            {
                columns = new List<string>();
                columns.Add(GetCSVReasonString(reasonString));
                foreach (Bucket bucket in reasonLookup[reasonString].Values)
                {
                    columns.Add(func(bucket));
                }
                fileContents.AppendLine(string.Join(",", columns));
            }

            string genericFilePath = Path.Combine(rootPath, fileName);
            string datedFilePath = Path.Combine(rootPath, GetLatestDate().ToShortDateString().Replace('/', '_') + "_" + fileName);
            File.WriteAllText(datedFilePath, fileContents.ToString(), Encoding.UTF8);
            File.Copy(datedFilePath, genericFilePath, overwrite: true);
        }

        private static DateTime GetLatestDate()
        {
            return reasonLookup[reasonStrings[0]].Keys.Max();
        }

        private static string GetCSVReasonString(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return "Silent";
            }

            if (reason.Contains(","))
            {
                return "\"" + reason + "\"";
            }

            return reason;
        }

        private static DateTime GetDateFromFilePath(string summaryOutputPath)
        {
            string[] parts = summaryOutputPath.Split(new char[] { '_' });
            Debug.Assert(parts.Length == 3);

            Debug.Assert(parts[0].Length >= 4);
            string yearString = parts[0].Substring(parts[0].Length - 4, 4);

            Debug.Assert(parts[1].Length == 2);
            string monthString = parts[1];

            Debug.Assert(parts[2].Length >= 2);
            string dayString = parts[2].Substring(0, 2);

            return new DateTime(int.Parse(yearString), int.Parse(monthString), int.Parse(dayString));
        }
    }
}
