using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CcExcelWriter;

namespace HealthSpreadsheet
{
    class Program
    {
        static string rootPath = @"\\geevens-server\incoming\Health-Spreadsheet";
        static Dictionary<string, Dictionary<DateTime, Bucket>> reasonLookup = new Dictionary<string, Dictionary<DateTime, Bucket>>();
        static string[] reasonStrings;

        static void Main(string[] args)
        {
            reasonStrings = File.ReadAllLines(Path.Combine(rootPath, "Reasons.txt"));
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
                        Console.ReadKey();
                        return;
                    }

                    reasonLookup[reasonString][date].AddFromBucket(bucket);
                }
            }

            // All bucket lists should have the same length
            Debug.Assert(reasonLookup.All(x => x.Value.Count == reasonLookup.First().Value.Count));

            DateTime latestDate = GetLatestDate();
            reasonStrings = reasonStrings.OrderBy(x => reasonLookup[x], new SpecialComparer()).ToArray();

            string genericFilePath = Path.Combine(rootPath, "DunningHealth.xlsx");
            string datedFilePath = Path.Combine(rootPath, GetLatestDate().ToShortDateString().Replace('/', '_') + ".xlsx");
            File.Copy(genericFilePath, datedFilePath, overwrite: true);

            WriteCSVByReason(x => x.Count, "Faulted-Count", datedFilePath);
            WriteCSVByReason(x => x.InvoiceValue, "Faulted-Invoice-Value", datedFilePath);
            WriteCSVByReason(x => x.ConsumerValue, "Faulted-Consumer-Value", datedFilePath);

            File.Copy(datedFilePath, genericFilePath, overwrite: true);
            Process.Start(genericFilePath);
        }

        private static void WriteCSVByReason(Func<Bucket, object> func, string sheetName, string filePath)
        {
            using (FileStream datedFile = new FileStream(filePath, FileMode.Open))
            {
                Excel datedExcel = new Excel(datedFile);
                Sheet datedSheet = datedExcel.GetSheet(sheetName);

                uint line = 0;
                CellStyle textStyle = datedSheet.GetCellStyle("A", line + 1);
                CellStyle dateStyle = datedSheet.GetCellStyle("B", line + 1);
                CellStyle numberStyle = datedSheet.GetCellStyle("B", line + 2);

                datedSheet.ClearAllSheetDataBelow(line++);

                BaseAZ column = BaseAZ.Parse("A");
                datedSheet.SetCell(column++, line, textStyle, "Reason");
                DateTime[] dates = reasonLookup[reasonStrings[0]].Keys.OrderByDescending(x => x).ToArray();
                foreach (DateTime date in dates)
                {
                    datedSheet.SetCell(column++, line, dateStyle, ToExcelDateValue(date));
                }
                line++;

                foreach (string reasonString in reasonStrings)
                {
                    column = BaseAZ.Parse("A");
                    datedSheet.SetCell(column++, line, textStyle, GetCSVReasonString(reasonString));
                    foreach (DateTime date in dates)
                    {
                        datedSheet.SetCell(column++, line, numberStyle, func(reasonLookup[reasonString][date]));
                    }
                    line++;
                }
                datedExcel.Save();
            }
        }

        private static double ToExcelDateValue(DateTime date)
        {
            return (date - new DateTime(1900, 1, 1)).TotalDays + 1;
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
