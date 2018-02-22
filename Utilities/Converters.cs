using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utilities
{
    public static class Converters
    {
        public static double? ToDouble(object obj)
        {
            if (obj.GetType() == typeof(int))
            {
                return (int)obj;
            }

            if (obj.GetType() == typeof(float))
            {
                return (float)obj;
            }

            if (obj.GetType() == typeof(double))
            {
                return (double)obj;
            }

            Debug.Fail("invalid double: " + obj.ToString());
            return null;
        }
    }
}
