using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace TextManipulator
{
    static class CSEscape
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
                    case 'n': retval.Append('\n'); break;  // Line feed
                    case 'r': retval.Append('\r'); break;  // Carriage return
                    case 't': retval.Append('\t'); break;  // Tab
                    case '\"': retval.Append('\"'); break; // quote
                    case '\\': retval.Append('\\'); break; // Don't escape
                    default:                                 // Unrecognized, copy as-is
                        retval.Append('\\').Append(input[jj + 1]); break;
                }
                ii = jj + 2;
            }

            string output = retval.ToString();
            Debug.Assert(Escape(output) == input);
            return output;

            //return Regex.Replace(input, @"\\[rnbf't]", m =>
            //{
            //    switch (m.Value)
            //    {
            //        case @"\r": return "\r";
            //        case @"\n": return "\n";
            //        case @"\t": return "\t";
            //        default: return m.Value;
            //    }
            //});

            //string output = input;
            //output = output.Replace("\\\\", "\\");
            //output = output.Replace("\\r", "\r");
            //output = output.Replace("\\n", "\n");
            //output = output.Replace("\\'", "\'");
            //output = output.Replace("\\\"", "\"");
            //output = output.Replace("\\b", "\b");
            //output = output.Replace("\\f", "\f");
            //Debug.Assert(Escape(output) == input);
            //return output;
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
                    case '\n': retval.Append(@"\n"); break;  // Line feed
                    case '\r': retval.Append(@"\r"); break;  // Carriage return
                    case '\t': retval.Append(@"\t"); break;  // Tab
                    case '\"': retval.Append("\\\""); break; // quote
                    case '\\': retval.Append("\\\\"); break; // Don't escape
                    default:                                 // Unrecognized, copy as-is
                        Debug.Fail("unknown char"); break;
                }
                ii = jj + 1;
            }

            string output = retval.ToString();
            //Debug.Assert(Unescape(output) == input);
            return output;

            //string output = input;
            //output = output.Replace("\\", "\\\\");
            //output = output.Replace("\r", "\\r");
            //output = output.Replace("\n", "\\n");
            //output = output.Replace("\'", "\\'");
            //output = output.Replace("\"", "\\\"");
            //output = output.Replace("\b", "\\b");
            //output = output.Replace("\f", "\\f");
            //return output;
        }
    }
}
