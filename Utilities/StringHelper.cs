using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public static string GetTrimmedString(string str, string start, string end)
        {
            IList<string> parts = SplitString(str, start, end);
            if (parts == null)
            {
                return string.Empty;
            }

            Debug.Assert(parts.Count == 3);
            return parts[1];
        }

        public static IList<string> SplitString(string str, string start, string end)
        {
            int startIndex = str.IndexOf(start);
            int endIndex = str.LastIndexOf(end);
            if (startIndex >= 0 && endIndex >= 0 && endIndex > startIndex && startIndex < str.Length && endIndex < str.Length)
            {
                string preString = str.Substring(0, startIndex);
                string trimmedStr = str.Substring(startIndex, endIndex - startIndex + 1);
                string postString = str.Substring(endIndex + 1);
                Debug.Assert(trimmedStr.StartsWith(start));
                Debug.Assert(trimmedStr.EndsWith(end));
                return new List<string>(new string[] { preString, trimmedStr, postString });
            }

            return null;
        }

        public static byte[] HexStringToByteArray(string hex)
        {
            if (hex.StartsWith("0x"))
            {
                hex = hex.Substring(2);
            }

            int charCount = hex.Length;

            byte[] bytes = new byte[charCount / 2];

            for (int i = 0; i < charCount; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }

            return bytes;
        }

        public static bool IsHexString(string str)
        {
            IEnumerable<char> enumString = str;

            if (str.StartsWith("0x"))
            {
                enumString = enumString.Skip(2);
            }

            return enumString.All(ch =>
            {
                return (ch >= '0' && ch <= '9') || ((ch = Char.ToLower(ch)) >= 'a' && ch <= 'f');
            });
        }
    }
}
