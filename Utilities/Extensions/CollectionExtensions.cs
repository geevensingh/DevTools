using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utilities.Extensions
{
    public static class CollectionExtensions
    {
        public static IEnumerable<IEnumerable<T>> GetPermutations<T>(this IEnumerable<T> list, int length)
        {
            if (length == 1)
            {
                return list.Select(t => new T[] { t });
            }

            return GetPermutations(list, length - 1)
                .SelectMany(t => list.Where(o => !t.Contains(o)),
                    (t1, t2) =>
                    {
                        return t1.Concat(new T[] { t2 });
                    });
        }

        public static async Task<IEnumerable<IEnumerable<T>>> GetPermutationsAsync<T>(this IEnumerable<T> list, int length)
        {
            if (length == 1)
            {
                return list.Select(t => new T[] { t });
            }

            var tasks = new List<Task<IEnumerable<IEnumerable<T>>>>();
            int count = list.Count();
            for (int ii = 0; ii < count; ii++)
            {
                tasks.Add(Task.Run(
                    async () =>
                    {
                        var newList = list.Take(ii).Skip(1).Take(count);
                        var singleItemList = list.Skip(ii).Take(1);
                        var newPermutations = await GetPermutationsAsync(newList, length - 1);
                        return newPermutations.Select(x => singleItemList.Concat(x));
                    }));

            }

            return (await Task.WhenAll(tasks)).SelectMany(x => x);
        }

        public static int IndexOf<T>(this T[] array, T item)
        {
            for (int ii = 0; ii < array.Length; ii++)
            {
                if (array[ii].Equals(item))
                {
                    return ii;
                }
            }

            return -1;
        }
    }
}
