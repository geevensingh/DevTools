using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace HealthSpreadsheet
{
    internal class SpecialComparer : IComparer<Dictionary<DateTime, Bucket>>
    {
        public int Compare(Dictionary<DateTime, Bucket> x, Dictionary<DateTime, Bucket> y)
        {
            Debug.Assert(x.Keys.Count == y.Keys.Count);
            foreach (DateTime date in x.Keys.OrderByDescending(datex => datex))
            {
                int result = x[date].Count.CompareTo(y[date].Count);
                if (result != 0)
                {
                    return -1 * result;
                }
            }

            return x.First().Value.Reason.CompareTo(y.First().Value.Reason);
        }
    }
}