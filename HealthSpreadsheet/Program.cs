using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using CcExcelWriter;

namespace HealthSpreadsheet
{
    class Program
    {
        static string rootPath = @"\\geevens-server\incoming\Health-Spreadsheet - Test";
        static Dictionary<string, Dictionary<DateTime, Bucket>> reasonLookup = new Dictionary<string, Dictionary<DateTime, Bucket>>();
        static List<string> reasonStrings;

        static void Main(string[] args)
        {
            reasonStrings = File.ReadAllLines(Path.Combine(rootPath, "Reasons.txt")).ToList();
            foreach (string reasonString in reasonStrings)
            {
                reasonLookup.Add(reasonString, new Dictionary<DateTime, Bucket>());
            }

            List<DateTime> allDates = new List<DateTime>();
            string[] summaryOutputPaths = Directory.EnumerateFiles(rootPath, @"AllUpBadSchedulesSummaryByReason????_??_??.ss.csv", SearchOption.TopDirectoryOnly).ToArray();
            foreach (string summaryOutputPath in summaryOutputPaths)
            {
                DateTime date = GetDateFromFilePath(summaryOutputPath);
                allDates.Add(date);

                string[] summaryOutputLines = File.ReadAllLines(summaryOutputPath);
                for (int ii = 1; ii < summaryOutputLines.Length; ii++)
                {
                    if (string.IsNullOrEmpty(summaryOutputLines[ii]))
                    {
                        continue;
                    }

                    Bucket bucket = Bucket.CreateFromLine(summaryOutputLines[0], summaryOutputLines[ii]);
                    string reasonString = bucket.GetBestReason(reasonStrings);
                    if (reasonString == null)
                    {
                        Console.WriteLine("Unable to determine the best bucket for " + bucket.Reason);
                        Console.ReadKey();
                        return;
                    }

                    reasonLookup[reasonString][date] = bucket;
                }
            }

            // Remove reasons that have never occured
            foreach (string reasonString in reasonStrings)
            {
                if (reasonLookup[reasonString].Count == 0)
                {
                    reasonLookup.Remove(reasonString);
                }
            }
            reasonStrings = reasonLookup.Keys.ToList();

            // Fill in buckets that didn't occur
            foreach (string reasonString in reasonStrings)
            {
                foreach (DateTime date in allDates)
                {
                    if (!reasonLookup[reasonString].ContainsKey(date))
                    {
                        reasonLookup[reasonString][date] = new Bucket(reasonString);
                    }
                }
            }

            // All bucket lists should have the same length
            Debug.Assert(reasonLookup.All(x => x.Value.Count == reasonLookup.First().Value.Count));

            // Make sure the dates are all the same
            allDates.Sort();
            List<DateTime> dateTimes = reasonLookup[reasonStrings[0]].Keys.ToList();
            dateTimes.Sort();
            Debug.Assert(allDates.Count == dateTimes.Count);
            for (int ii = 0; ii < allDates.Count; ii++)
            {
                Debug.Assert(allDates[ii] == dateTimes[ii]);
            }

            // Figure out the diff between the last 2 dates
            DateTime latestDate = allDates.Last();
            DateTime nextLatestDate = allDates.Where(x => x != latestDate).Last();
            Dictionary<string, Bucket> diffs = new Dictionary<string, Bucket>();
            foreach (string reasonString in reasonStrings)
            {
                Dictionary<DateTime, Bucket> x = reasonLookup[reasonString];
                diffs.Add(reasonString, Bucket.Diff(x[latestDate], x[nextLatestDate], reasonStrings));
            }

            // Order the reasons by SpecialComparer (probably by count)
            reasonStrings = reasonStrings.OrderBy(x => reasonLookup[x], new SpecialComparer()).ToList();

            string genericFilePath = Path.Combine(rootPath, "DunningHealth.xlsx");
            string datedFilePath = Path.Combine(rootPath, GetLatestDate().ToShortDateString().Replace('/', '_') + ".xlsx");
            File.Copy(genericFilePath, datedFilePath, overwrite: true);

            WriteSheet(diffs, x => x.Count, "Faulted-Count", datedFilePath);
            WriteSheet(diffs, x => x.InvoiceValue, "Faulted-Invoice-Value", datedFilePath);
            WriteSheet(diffs, x => x.ConsumerValue, "Faulted-Consumer-Value", datedFilePath);

            File.Copy(datedFilePath, genericFilePath, overwrite: true);
            Process.Start(genericFilePath);
        }

        private static void WriteSheet(Dictionary<string, Bucket> diffs, Func<Bucket, object> func, string sheetName, string filePath)
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
                    datedSheet.SetCell(column++, line, textStyle, GetReasonString(diffs, func, reasonString));
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
            return (date - new DateTime(1900, 1, 1)).TotalDays + 2;
        }

        private static DateTime GetLatestDate()
        {
            return reasonLookup[reasonStrings[0]].Keys.Max();
        }

        private static string GetReasonString(Dictionary<string, Bucket> diffs, Func<Bucket, object> func, string reason)
        {
            string prefixString = string.Empty;
            object prefixObject = func(diffs[reason]);
            if (prefixObject is int prefixInt && prefixInt != 0)
            {
                prefixString = prefixInt.ToString();
            }
            else if (prefixObject is decimal prefixDecimal && prefixDecimal != 0m)
            {
                NumberFormatInfo current = (NumberFormatInfo)NumberFormatInfo.CurrentInfo.Clone();
                current.CurrencyNegativePattern = 1;    // -$n
                prefixString = string.Format(current, "{0:C}", prefixDecimal);
            }

            if (!string.IsNullOrEmpty(prefixString))
            {
                prefixString += " : ";
            }

            if (string.IsNullOrEmpty(reason))
            {
                reason = "Silent";
            }

            return prefixString + reason;
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
