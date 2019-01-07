using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HealthSpreadsheet
{
    class Bucket
    {
        private string reason;
        private uint count;
        private decimal consumerValue;
        private decimal invoiceValue;

        public decimal InvoiceValue { get => invoiceValue; }
        public decimal ConsumerValue { get => consumerValue; }
        public uint Count { get => count; }
        public string Reason { get => reason; }

        public Bucket(string reason)
        {
            this.reason = reason;
            this.count = 0;
            this.consumerValue = 0m;
            this.invoiceValue = 0m;
        }

        public static Bucket CreateFromLine(DateTime date, string header, string line)
        {
            if (header == "Reason,Count,TotalUSD")
            {
                // inject a 0 value at the end for CommercialUSDImpact so that we treat TotalUSD as ConsumerUSDImpact
                line += ",0";
            }
            else
            {
                Debug.Assert(header == "Reason,Count,ConsumerUSDImpact,CommercialUSDImpact");
            }

            string[] parts = line.Split(new char[] { ',' }, StringSplitOptions.None);
            Debug.Assert(parts.Length >= 4);
            
            return new Bucket(parts.Take(parts.Length - 3).Aggregate((agg, x) => agg + "," + x))
            {
                invoiceValue = (decimal)float.Parse(parts[parts.Length - 1]),
                consumerValue = (decimal)float.Parse(parts[parts.Length - 2]),
                count = uint.Parse(parts[parts.Length - 3]),
            };
        }

        public string GetBestReason(string[] reasonStrings)
        {
            foreach (string reasonString in reasonStrings)
            {
                if (reasonString == this.reason)
                {
                    return reasonString;
                }
            }

            foreach (string reasonString in reasonStrings)
            {
                if (!string.IsNullOrEmpty(reasonString) && this.reason.IndexOf(reasonString) != -1)
                {
                    return reasonString;
                }
            }

            return null;
        }

        public void AddFromBucket(Bucket bucket)
        {
            this.invoiceValue += bucket.invoiceValue;
            this.consumerValue += bucket.consumerValue;
            this.count += bucket.count;
        }
    }
}
