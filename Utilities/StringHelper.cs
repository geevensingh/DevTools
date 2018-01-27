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

        public static string TrimEnd(string fullString, string suffix)
        {
            return TrimEnd(fullString, suffix, StringComparison.CurrentCulture);
        }

        public static string TrimEnd(string fullString, string suffix, StringComparison comparisonType)
        {
            int index = fullString.LastIndexOf(suffix, comparisonType);
            if (fullString.Length - suffix.Length == index)
            {
                return fullString.Substring(0, index);
            }
            return fullString;
        }

        public static bool AnyLineContains(string[] lines, string str)
        {
            if (lines != null)
            {
                foreach (string line in lines)
                {
                    if (line.Contains(str))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool AnyLineIs(string[] lines, string str)
        {
            if (lines != null)
            {
                foreach (string line in lines)
                {
                    if (line == str)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool EndsWithAny(string text, string[] suffixes)
        {
            foreach(string suffix in suffixes)
            {
                if (text.EndsWith(suffix))
                {
                    return true;
                }
            }
            return false;
        }

        public static string GeneratePrefix(int depth, string substr = "\t")
        {
            StringBuilder sb = new StringBuilder();
            for (int ii = 0; ii < depth; ii++)
            {
                sb.Append(substr);
            }
            return sb.ToString();
        }

    }
}
