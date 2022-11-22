using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeaponEvaluator
{
    public static class Extensions
    {
        public static string? TrimUpToAndIncluding(this string? source, string trim)
        {
            int index = source?.IndexOf(trim) ?? -1;
            if (index == -1)
            {
                return null;
            }

            return source.Substring(index + trim.Length, source.Length - index - trim.Length);
        }

        public static string? TrimAtAndAfter(this string? source, string trim)
        {
            int index = source?.IndexOf(trim) ?? -1;
            if (index == -1)
            {
                return null;
            }

            return source.Substring(0, index);
        }
        public static string Foo(this string source)
        {
            if (source.Contains(" "))
            {
                return '\"' + source + '\"';
            }

            return source;
        }

        public static string TrimStart(this string fullString, string prefix)
        {
            return TrimStart(fullString, prefix, StringComparison.CurrentCulture);
        }

        public static string TrimStart(this string fullString, string prefix, StringComparison comparisonType)
        {
            if (fullString.IndexOf(prefix, comparisonType) == 0)
            {
                return fullString.Substring(prefix.Length);
            }
            return fullString;
        }

    }
}
