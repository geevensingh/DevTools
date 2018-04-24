using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsvHelper;

namespace Fix_MachineName
{
    class Program
    {
        static void Main(string[] args)
        {
            string filePath = @"C:\Users\geevens\Downloads\02c93b10318c026bb8b5f5cc33c31287.csv";
            CsvReader csvReader = new CsvReader(new StreamReader(filePath));
            Debug.Assert(csvReader.Read());
            Debug.Assert(csvReader.ReadHeader());
            var field = csvReader[0];

            Dictionary<int, string> machineLookup = new Dictionary<int, string>();
            List<dynamic> records = new List<dynamic>(csvReader.GetRecords<dynamic>());
            foreach (dynamic record in records)
            {
                string machineName = record.MachineName;
                if (!string.IsNullOrEmpty(machineName))
                {
                    int processId = int.Parse(record.ProcessID);
                    Debug.Assert(!machineLookup.ContainsKey(processId) || machineLookup[processId] == machineName);
                    machineLookup[processId] = machineName;
                }
            }

            foreach (dynamic record in records)
            {
                string machineName = record.MachineName;
                if (string.IsNullOrEmpty(machineName))
                {
                    int processId = int.Parse(record.ProcessID);
                    record.MachineName = machineLookup[processId];
                }
            }

            csvReader.Dispose();
            csvReader = null;

            CsvWriter csvWriter = new CsvWriter(new StreamWriter(filePath));
            csvWriter.WriteRecords(records);


            //string[] lines = File.ReadAllLines(filePath);
            //List<List<string>> table = new List<List<string>>();
            //for (int ii = 0; ii < lines.Length; ii++)
            //{
            //    string line = lines[ii];
            //    Debug.Assert(table.Count == ii);
            //    table.Add(new List<string>(line.Split(new char[] { ',' })));

            //}
        }
    }
}
