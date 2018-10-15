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
            List<string> warningMessages = new List<string>();
            accountId = Request.Form["accountId"].Trim();
            scheduleId = Request.Form["scheduleId"].Trim();

            if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(scheduleId))
            {
                string data = string.IsNullOrWhiteSpace(accountId) ? scheduleId : accountId;
                if (!TryParseAccountIdAndScheduleId(data, ref accountId, ref scheduleId))
                {
                    return false;
                }
            }

            Guid temp;
            if (!Guid.TryParseExact(accountId, "D", out temp))
            {
                warningMessages.Add(string.Format("AccountId does not seem to be in the right format: {0}", accountId));
            }

            if (!Guid.TryParseExact(scheduleId, "N", out temp))
            {
                warningMessages.Add(string.Format("ScheduleId does not seem to be in the right format: {0}", scheduleId));
            }

            if (warningMessages.Count > 0)
            {
                ViewBag.WarningMessage = string.Join("\r\n", warningMessages.ToArray());
            }

            return true;
        }

        private bool TryParseAccountIdAndScheduleId(string data, ref string accountId, ref string scheduleId)
        {
            char[] delimiters = new char[] { '/', ',', '\t', ' ', '\r', '\n' };
            data = data.Trim(delimiters);

            if (string.IsNullOrWhiteSpace(data))
            {
                ViewBag.ErrorMessage = "AccountId and ScheduleId are both empty.  No data found.";
                return false;
            }

            if (data.IndexOfAny(delimiters) != -1)
            {
                List<string> parts = new List<string>(data.Split(delimiters, StringSplitOptions.RemoveEmptyEntries));
                parts.Remove("capture-schedules");
                if (parts.Count != 2)
                {
                    ViewBag.ErrorMessage = string.Format("Unable to parse data: {0}", data);
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

            return true;
        }
    }
}
