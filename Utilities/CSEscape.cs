namespace Utilities
{
    using System.Diagnostics;
    using System.Text;

    public static class CSEscape
    {
        public static string Unescape(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            StringBuilder retval = new StringBuilder(input.Length);
            for (int ii = 0; ii < input.Length;)
            {
                int jj = input.IndexOf('\\', ii);
                if (jj < 0 || jj == input.Length - 1)
                {
                    jj = input.Length;
                }

                retval.Append(input, ii, jj - ii);
                if (jj >= input.Length)
                {
                    break;
                }

                switch (input[jj + 1])
                {
                    // Line feed
                    case 'n':
                        retval.Append('\n');
                        break;

                    // Carriage return
                    case 'r':
                        retval.Append('\r');
                        break;

                    // Tab
                    case 't':
                        retval.Append('\t');
                        break;

                    // quote
                    case '\"':
                        retval.Append('\"');
                        break;

                    // Don't escape
                    case '\\':
                        retval.Append('\\');
                        break;

                    // Unrecognized, copy as-is
                    default:
                        retval.Append('\\').Append(input[jj + 1]);
                        break;
                }

                ii = jj + 2;
            }

            return retval.ToString();
        }

        public static string Escape(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            StringBuilder retval = new StringBuilder(input.Length);
            for (int ii = 0; ii < input.Length;)
            {
                int jj = input.IndexOfAny(new char[] { '\n', '\r', '\t', '\"', '\\' }, ii);
                if (jj < 0 || jj == input.Length - 1)
                {
                    jj = input.Length;
                }

                retval.Append(input, ii, jj - ii);
                if (jj >= input.Length)
                {
                    break;
                }

                switch (input[jj])
                {
                    // Line feed
                    case '\n':
                        retval.Append(@"\n");
                        break;

                    // Carriage return
                    case '\r':
                        retval.Append(@"\r");
                        break;

                    // Tab
                    case '\t':
                        retval.Append(@"\t");
                        break;

                    // quote
                    case '\"':
                        retval.Append("\\\"");
                        break;

                    // Don't escape
                    case '\\':
                        retval.Append("\\\\");
                        break;

                    // Unrecognized, copy as-is
                    default:
                        Debug.Fail("unknown char"); break;
                }

                ii = jj + 1;
            }

            string output = retval.ToString();
            Debug.Assert(Unescape(output) == input);
            return output;
        }
    }
}
