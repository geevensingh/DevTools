using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArmorEvaluator
{
    public static class CollectionExtensions
    {
        public static int RemoveSet<T>(this HashSet<T> set, IEnumerable<T> toRemove)
        {
            var list = toRemove.ToList();
            foreach (var item in list)
            {
                Debug.Assert(set.Remove(item));
            }
            return list.Count;
        }
    }
}
