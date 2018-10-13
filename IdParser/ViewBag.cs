using System;

namespace IdParser
{
    public class ViewBag
    {
        public string ErrorMessage { get; internal set; }
        public string WarningMessage { get; internal set; }

        public ViewBag()
        {
            this.ErrorMessage = string.Empty;
            this.WarningMessage = string.Empty;
        }
    }
}