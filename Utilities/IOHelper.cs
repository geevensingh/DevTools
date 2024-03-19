using System.Text;
using System.IO;
using System;

namespace Utilities
{
    public class IOHelper
    {
        /// <summary>
        /// Determines a text file's encoding by analyzing its byte order mark (BOM).
        /// Defaults to ASCII when detection of the text file's endianness fails.
        /// </summary>
        /// <param name="filename">The text file to analyze.</param>
        /// <returns>The detected encoding.</returns>
        public static Encoding GetEncoding(string filename)
        {
            // Read the BOM
            var bom = new byte[4];
            using (var file = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                file.Read(bom, 0, 4);
            }

            // Analyze the BOM
            if (bom[0] == 0x2b && bom[1] == 0x2f && bom[2] == 0x76) return Encoding.UTF7;
            if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf) return Encoding.UTF8;
            if (bom[0] == 0xff && bom[1] == 0xfe) return Encoding.Unicode; //UTF-16LE
            if (bom[0] == 0xfe && bom[1] == 0xff) return Encoding.BigEndianUnicode; //UTF-16BE
            if (bom[0] == 0 && bom[1] == 0 && bom[2] == 0xfe && bom[3] == 0xff) return Encoding.UTF32;
            return Encoding.ASCII;
        }

        public static void WriteAllText(string path, string contents)
        {
            bool newContentEndsWithCRLF = contents.EndsWith("\r\n");
            bool oldContentEndsWithCRLF = File.ReadAllText(path).EndsWith("\r\n");
            if (newContentEndsWithCRLF != oldContentEndsWithCRLF)
            {
                if (newContentEndsWithCRLF)
                {
                    contents = StringExtensions.TrimEnd(contents, "\r\n");
                }
                else
                {
                    contents += "\r\n";
                }
            }

            File.WriteAllText(path, contents, GetEncoding(path));
        }

        public static bool IsSubdirectory(string parentPath, string childPath)
        {
            Uri parentUri = new Uri(parentPath);
            DirectoryInfo childUri = new DirectoryInfo(childPath);
            while (childUri != null)
            {
                if (new Uri(childUri.FullName) == parentUri)
                {
                    return true;
                }
                childUri = childUri.Parent;
            }
            return false;
        }
    }
}
