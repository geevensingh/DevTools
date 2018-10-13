using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IdParser
{
    public class PublicIdParser : IdParser
    {
        public static bool TryParse(ref string accountId, ref string scheduleId, out PublicIdParser publicIdParser)
        {
            publicIdParser = new PublicIdParser();
            publicIdParser.Request.Form["accountId"] = accountId;
            publicIdParser.Request.Form["scheduleId"] = scheduleId;

            return publicIdParser.TryParseAccountIdAndScheduleId(out accountId, out scheduleId);
        }
    }
}
