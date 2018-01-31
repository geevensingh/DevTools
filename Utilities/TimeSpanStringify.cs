using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Utilities
{
    public class TimeSpanStringify
    {
        public static string PrettyApprox(TimeSpan timeSpan, int decimals = 1)
        {
            string suffix = " ago";
            if (timeSpan.TotalMilliseconds < 0)
            {
                suffix = " from now";
                timeSpan = timeSpan.Negate();
            }

            string str = timeSpan.ToString();
            if (timeSpan.TotalDays > 1)
            {
                str = Math.Round(timeSpan.TotalDays, decimals) + " days";
            }
            else if (timeSpan.TotalHours > 0)
            {
                str = Math.Round(timeSpan.TotalHours, decimals) + " hours";
            }
            else if (timeSpan.TotalMinutes > 0)
            {
                str = Math.Round(timeSpan.TotalMinutes, decimals) + " minutes";
            }
            else if (timeSpan.TotalSeconds > 0)
            {
                str = Math.Round(timeSpan.TotalSeconds, decimals) + " seconds";
            }
            else if (timeSpan.TotalMilliseconds > 0)
            {
                str = Math.Round(timeSpan.TotalMilliseconds, decimals) + " milliseconds";
            }
            else
            {
                Debug.Assert(false);
            }

            return str + suffix;
        }

        public static string PrettyExact(TimeSpan timeSpan, int decimals = 5)
        {
            if (timeSpan.TotalDays > 1)
            {
                return Math.Round(timeSpan.TotalDays, decimals) + " days";
            }

            if (timeSpan.TotalHours > 0)
            {
                return Math.Round(timeSpan.TotalHours, decimals) + " hours";
            }

            if (timeSpan.TotalMinutes > 0)
            {
                return Math.Round(timeSpan.TotalMinutes, decimals) + " minutes";
            }

            if (timeSpan.TotalSeconds > 0)
            {
                return Math.Round(timeSpan.TotalSeconds, decimals) + " seconds";
            }

            if (timeSpan.TotalMilliseconds > 0)
            {
                return Math.Round(timeSpan.TotalMilliseconds, decimals) + " milliseconds";
            }

            Debug.Assert(false);
            return timeSpan.ToString();
        }
    }
}
