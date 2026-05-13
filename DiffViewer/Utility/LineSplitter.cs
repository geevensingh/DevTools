namespace DiffViewer.Utility;

/// <summary>
/// Splits a string into lines while preserving each line's exact trailing
/// terminator (CRLF / LF / CR / none). This is required for byte-exact
/// reconstruction in <see cref="UnifiedDiffFormatter"/> — a patch whose
/// terminators don't match the source bytes will be rejected by
/// <c>git apply</c> ("patch does not apply" / "while searching for...").
/// </summary>
public static class LineSplitter
{
    /// <summary>One line of source: text without terminator + the original terminator bytes.</summary>
    public readonly record struct Line(string Text, string Terminator)
    {
        /// <summary>True if the line was the trailing line of the source and lacked a terminator.</summary>
        public bool IsLastWithoutTerminator => Terminator.Length == 0;
    }

    /// <summary>
    /// Split <paramref name="source"/> into lines, preserving each line's
    /// exact terminator. The final element has an empty
    /// <see cref="Line.Terminator"/> iff the source did not end with a
    /// newline (i.e. <c>\ No newline at end of file</c> applies).
    /// </summary>
    /// <remarks>
    /// An <em>empty source</em> yields an empty list, not a single empty
    /// line. An empty source diff'd against itself produces zero hunks.
    /// </remarks>
    public static IReadOnlyList<Line> Split(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return Array.Empty<Line>();
        }

        var result = new List<Line>(capacity: 64);
        int i = 0;
        int lineStart = 0;
        while (i < source.Length)
        {
            char c = source[i];
            if (c == '\r')
            {
                if (i + 1 < source.Length && source[i + 1] == '\n')
                {
                    result.Add(new Line(source.Substring(lineStart, i - lineStart), "\r\n"));
                    i += 2;
                    lineStart = i;
                }
                else
                {
                    result.Add(new Line(source.Substring(lineStart, i - lineStart), "\r"));
                    i += 1;
                    lineStart = i;
                }
            }
            else if (c == '\n')
            {
                result.Add(new Line(source.Substring(lineStart, i - lineStart), "\n"));
                i += 1;
                lineStart = i;
            }
            else
            {
                i += 1;
            }
        }

        if (lineStart < source.Length)
        {
            // Trailing partial line (no terminator).
            result.Add(new Line(source.Substring(lineStart), string.Empty));
        }

        return result;
    }
}
