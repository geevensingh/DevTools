using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utilities
{
    public class StringHelper
    {
        public static string[] ToLower(string[] input)
        {
            List<string> output = new List<string>();
            foreach(string str in input)
            {
                output.Add(str.ToLower());
            }
            return output.ToArray();
        }

        public static string TrimStart(string fullString, string prefix)
        {
            return TrimStart(fullString, prefix, StringComparison.CurrentCulture);
        }

        public static string TrimStart(string fullString, string prefix, StringComparison comparisonType)
        {
            if (fullString.IndexOf(prefix, comparisonType) == 0)
            {
                return fullString.Substring(prefix.Length);
            }
            return fullString;
        }
    }
}
