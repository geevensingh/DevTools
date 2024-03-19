using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LetterLongDivision
{
    public static class IntExtensions
    {
        public static int Start(this int value, int count)
        {
            return int.Parse(value.ToString().Substring(0, count));
        }

        public static int Length(this int value)
        {
            return value.ToString().Length;
        }

        public static int GetDigit(this int value, int index)
        {
            return int.Parse(value.ToString()[index].ToString());
        }

        public static int AddPrefix(this int value, int prefix)
        {
            string valueString = value.ToString();
            return prefix * (int)Math.Pow(10, valueString.Length) + value;
        }

        public static int GetSuffix(this int value, int startIndex)
        {
            string str = value.ToString().Substring(startIndex);
            if (str.Length == 0)
            {
                return -1;
            }
            return int.Parse(str);
        }
    }
}
