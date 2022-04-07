using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArmorEvaluator
{
    internal class Comparer : IEqualityComparer<ScratchPad>
    {
        public bool Equals(ScratchPad? x, ScratchPad? y)
        {
            return x?.Item.Id == y?.Item.Id;
        }

        public int GetHashCode([DisallowNull] ScratchPad obj)
        {
            return obj.Item.Id.GetHashCode();
        }
    }
}
