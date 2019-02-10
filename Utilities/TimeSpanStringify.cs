using System;

namespace Utilities
{
    public class TimeSpanStringify
    {
        public static string PrettyApprox(TimeSpan timeSpan, int decimals = 1)
        {
            string suffix = " from now";
            if (timeSpan.TotalMilliseconds < 0)
            {
                suffix = " ago";
                timeSpan = timeSpan.Negate();
            }

            string str = timeSpan.ToString();
            if (timeSpan.TotalDays > 1)
            {
                str = Math.Round(timeSpan.TotalDays, decimals) + " days";
            }
            else if (timeSpan.TotalHours > 1)
            {
                str = Math.Round(timeSpan.TotalHours, decimals) + " hours";
            }
            else if (timeSpan.TotalMinutes > 1)
            {
                str = Math.Round(timeSpan.TotalMinutes, decimals) + " minutes";
            }
            else if (timeSpan.TotalSeconds > 1)
            {
                str = Math.Round(timeSpan.TotalSeconds, decimals) + " seconds";
            }
            else if (timeSpan.TotalMilliseconds > 1)
            {
                str = Math.Round(timeSpan.TotalMilliseconds, decimals) + " milliseconds";
            }
            else
            {
                return "right now";
            }

            return str + suffix;
        }
    }
}
