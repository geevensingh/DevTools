using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Utilities
{
    public class StreamHelper
    {
        public static string ReadUTF8Line(Stream stream)
        {
            List<byte> bytes = new List<byte>(64);
            int result;
            while ((result = stream.ReadByte()) != -1)
            {
                if (result == '\r')
                {
                    result = stream.ReadByte();
                    Debug.Assert(result == '\n', @"The line does not end with a \r\n");
                    break;
                }

                bytes.Add((byte)result);
            }

            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }
}
