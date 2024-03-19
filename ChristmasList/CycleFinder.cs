using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChristmasList
{
    internal class CycleFinder
    {
        private Dictionary<string, string> lookup = new Dictionary<string, string>();
        private List<List<string>> cycles = new List<List<string>>();

        public CycleFinder(List<string> index, List<string> other)
        {
            Debug.Assert(index.Count == other.Count);
            this.lookup = new Dictionary<string, string>();
            for (int ii = 0; ii < index.Count; ii++)
            {
                this.lookup[index[ii]] = other[ii];
            }

            for (int ii = 0; ii < index.Count; ii++)
            {
                this.FindCycle(index[ii], new List<string>());
            }
        }

        public int MinCycleLength => this.cycles.Min(x => x.Count);

        private void FindCycle(string start, List<string> list)
        {
            if (list.Contains(start))
            {
                this.cycles.Add(list);
                return;
            }

            var clone = new List<string>(list)
            {
                start
            };
            this.FindCycle(this.lookup[start], clone);
        }
    }
}
