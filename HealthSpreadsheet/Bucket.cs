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
        private int count;
        private decimal consumerValue;
        private decimal invoiceValue;
        private decimal eaValue;

        public decimal InvoiceValue { get => invoiceValue; }
        public decimal EAValue { get => eaValue; }
        public decimal ConsumerValue { get => consumerValue; }
        public int Count { get => count; }
        public string Reason { get => reason; }

        public Bucket(string reason)
        {
            this.reason = reason;
            this.count = 0;
            this.consumerValue = 0m;
            this.invoiceValue = 0m;
            this.eaValue = 0m;
        }

        public static Bucket CreateFromLine(string header, string line)
        {
            if (header == "Reason,Count,TotalUSD")
            {
                // If we only have TotalUSD, then treat it all like ConsumerUSDImpact and
                // set NonEAInvoiceUSDImpact and EAInvoiceUSDImpact to 0
                line += ",0,0";
            }
            else if (header == "Reason,Count,ConsumerUSDImpact,CommercialUSDImpact")
            {
                // If we only have ConsumerUSDImpact and CommercialUSDImpact, then treat the
                // ConsumerUSDImpact as EAInvoiceUSDImpact and set NonEAInvoiceUSDImpact to 0.
                line = line.Insert(line.LastIndexOf(','), ",0");
            }
            else
            {
                Debug.Assert(header == "Reason,Count,ConsumerUSDImpact,NonEAInvoiceUSDImpact,EAInvoiceUSDImpact");
            }

            string[] parts = line.Split(new char[] { ',' }, StringSplitOptions.None);
            Debug.Assert(parts.Length >= 5);
            
            return new Bucket(parts.Take(parts.Length - 4).Aggregate((agg, x) => agg + "," + x))
            {
                eaValue = (decimal)float.Parse(parts[parts.Length - 1]),
                invoiceValue = (decimal)float.Parse(parts[parts.Length - 2]),
                consumerValue = (decimal)float.Parse(parts[parts.Length - 3]),
                count = int.Parse(parts[parts.Length - 4]),
            };
        }

        public string GetBestReason(List<string> reasonStrings)
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

        public static Bucket Diff(Bucket minuend, Bucket subtrahend, List<string> reasonStrings)
        {
            Debug.Assert(minuend.GetBestReason(reasonStrings) == subtrahend.GetBestReason(reasonStrings));
            return new Bucket(minuend.GetBestReason(reasonStrings))
            {
                count = minuend.Count - subtrahend.Count,
                consumerValue = minuend.ConsumerValue - subtrahend.ConsumerValue,
                invoiceValue = minuend.InvoiceValue - subtrahend.InvoiceValue,
                eaValue = minuend.EAValue - subtrahend.EAValue,
            };
        }

        public void AddFromBucket(Bucket bucket)
        {
            this.eaValue += bucket.eaValue;
            this.invoiceValue += bucket.invoiceValue;
            this.consumerValue += bucket.consumerValue;
            this.count += bucket.count;
        }
    }
}
