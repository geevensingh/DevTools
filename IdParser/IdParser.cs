using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IdParser
{
    public class IdParser
    {
        public Request Request { get; }
        public ViewBag ViewBag { get; }

        protected IdParser()
        {
            this.Request = new Request();
            this.ViewBag = new ViewBag();
        }

        protected bool TryParseAccountIdAndScheduleId(out string accountId, out string scheduleId)
        {
            accountId = Request.Form["accountId"].Trim();
            scheduleId = Request.Form["scheduleId"].Trim();

            if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(scheduleId))
            {
                string data = string.IsNullOrWhiteSpace(accountId) ? scheduleId : accountId;

                char[] delimiters = new char[] { '/', ',', '\t', ' ', '\r', '\n' };
                data = data.Trim(delimiters);

                if (string.IsNullOrWhiteSpace(data))
                {
                    ViewBag.ErrorMessage = "AccountId and ScheduleId are both empty.  No data found.";
                    return false;
                }

                if (accountId.IndexOfAny(delimiters) != -1)
                {
                    if (!string.IsNullOrWhiteSpace(scheduleId))
                    {
                        ViewBag.ErrorMessage = "If all data is given in accountId field, then scheduleId must be empty.";
                        return false;
                    }

                    List<string> parts = new List<string>(accountId.Split(delimiters, StringSplitOptions.RemoveEmptyEntries));
                    parts.Remove("capture-schedules");
                    if (parts.Count != 2)
                    {
                        ViewBag.ErrorMessage = string.Format("Unable to parse accountId: {0}", accountId);
                        return false;
                    }

                    accountId = parts[0];
                    scheduleId = parts[1];
                }

                if (string.IsNullOrWhiteSpace(accountId))
                {
                    ViewBag.ErrorMessage = "Could not find an accountId. Valid accountId required.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(scheduleId))
                {
                    ViewBag.ErrorMessage = "Could not find a scheduleId. Valid scheduleId required.";
                    return false;
                }
            }

            Guid temp;
            if (!Guid.TryParseExact(accountId, "D", out temp))
            {
                ViewBag.WarningMessage = string.Format("AccountId does not seem to be in the right format: {0}", accountId);
            }
            else if (!Guid.TryParseExact(scheduleId, "N", out temp))
            {
                ViewBag.WarningMessage = string.Format("ScheduleId does not seem to be in the right format: {0}", scheduleId);
            }

            return true;
        }
    }
}
