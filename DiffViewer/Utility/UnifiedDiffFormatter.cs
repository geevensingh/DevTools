using System.Globalization;
using System.Text;
using DiffViewer.Models;

namespace DiffViewer.Utility;

/// <summary>
/// Converts a sequence of <see cref="DiffHunk"/> records into git-compatible
/// unified-diff text. The byte-exact line endings of the source strings are
/// preserved on every emitted <c>-</c>/<c>+</c>/context line so that the
/// resulting patch applies cleanly via <c>git apply</c> against either a
/// CRLF or LF working tree.
/// </summary>
/// <remarks>
/// The <c>\ No newline at end of file</c> marker is emitted whenever the
/// source line being represented is the trailing line of its buffer and that
/// buffer does not end with a newline. The marker's exact byte sequence is
/// the literal text "\\ No newline at end of file" followed by an LF — git's
/// <c>apply</c> parser is strict about this format.
/// </remarks>
public static class UnifiedDiffFormatter
{
    private const string NoNewlineMarker = "\\ No newline at end of file\n";

    /// <summary>
    /// Format <paramref name="hunks"/> as a unified-diff string with the
    /// standard <c>--- a/&lt;path&gt;</c> / <c>+++ b/&lt;path&gt;</c> headers.
    /// </summary>
    /// <param name="oldPath">Repo-relative path on the left side (forward slashes).</param>
    /// <param name="newPath">Repo-relative path on the right side (forward slashes).</param>
    /// <param name="hunks">Hunks in source order.</param>
    /// <param name="leftSource">Original left-side text (used to recover byte-exact line terminators).</param>
    /// <param name="rightSource">Original right-side text (used to recover byte-exact line terminators).</param>
    public static string Format(
        string oldPath,
        string newPath,
        IReadOnlyList<DiffHunk> hunks,
        string leftSource,
        string rightSource)
    {
        ArgumentNullException.ThrowIfNull(oldPath);
        ArgumentNullException.ThrowIfNull(newPath);
        ArgumentNullException.ThrowIfNull(hunks);
        leftSource ??= string.Empty;
        rightSource ??= string.Empty;

        var leftLines = LineSplitter.Split(leftSource);
        var rightLines = LineSplitter.Split(rightSource);

        var builder = new StringBuilder(capacity: 256 + hunks.Sum(h => h.Lines.Count) * 64);

        builder.Append("--- a/").Append(oldPath).Append('\n');
        builder.Append("+++ b/").Append(newPath).Append('\n');

        foreach (var hunk in hunks)
        {
            AppendHunk(builder, hunk, leftLines, rightLines);
        }

        return builder.ToString();
    }

    private static void AppendHunk(
        StringBuilder builder,
        DiffHunk hunk,
        IReadOnlyList<LineSplitter.Line> leftLines,
        IReadOnlyList<LineSplitter.Line> rightLines)
    {
        builder.Append("@@ -")
            .Append(hunk.OldStartLine.ToString(CultureInfo.InvariantCulture))
            .Append(',')
            .Append(hunk.OldLineCount.ToString(CultureInfo.InvariantCulture))
            .Append(" +")
            .Append(hunk.NewStartLine.ToString(CultureInfo.InvariantCulture))
            .Append(',')
            .Append(hunk.NewLineCount.ToString(CultureInfo.InvariantCulture))
            .Append(" @@");

        if (!string.IsNullOrEmpty(hunk.FunctionContext))
        {
            builder.Append(' ').Append(hunk.FunctionContext);
        }

        builder.Append('\n');

        foreach (var line in hunk.Lines)
        {
            switch (line.Kind)
            {
                case DiffLineKind.Context:
                    AppendLineWithSourceTerminator(builder, ' ', line.Text,
                        SelectSource(line, leftLines, rightLines, preferLeft: true));
                    break;

                case DiffLineKind.Deleted:
                case DiffLineKind.Modified when line.OldLineNumber is not null && line.NewLineNumber is null:
                    AppendLineWithSourceTerminator(builder, '-', line.Text,
                        LookupLine(leftLines, line.OldLineNumber));
                    break;

                case DiffLineKind.Inserted:
                case DiffLineKind.Modified when line.OldLineNumber is null && line.NewLineNumber is not null:
                    AppendLineWithSourceTerminator(builder, '+', line.Text,
                        LookupLine(rightLines, line.NewLineNumber));
                    break;

                case DiffLineKind.Modified:
                    // Both line numbers populated: emit as a delete then an insert (git unified
                    // format has no "modified" concept). The renderer is responsible for the
                    // visual pairing.
                    AppendLineWithSourceTerminator(builder, '-', line.Text,
                        LookupLine(leftLines, line.OldLineNumber));
                    AppendLineWithSourceTerminator(builder, '+', line.Text,
                        LookupLine(rightLines, line.NewLineNumber));
                    break;
            }
        }
    }

    private static LineSplitter.Line? SelectSource(
        DiffLine line,
        IReadOnlyList<LineSplitter.Line> leftLines,
        IReadOnlyList<LineSplitter.Line> rightLines,
        bool preferLeft)
    {
        if (preferLeft && line.OldLineNumber is int leftIdx)
        {
            return LookupLine(leftLines, leftIdx);
        }

        if (line.NewLineNumber is int rightIdx)
        {
            return LookupLine(rightLines, rightIdx);
        }

        if (line.OldLineNumber is int leftFallback)
        {
            return LookupLine(leftLines, leftFallback);
        }

        return null;
    }

    private static LineSplitter.Line? LookupLine(IReadOnlyList<LineSplitter.Line> lines, int? oneBasedIndex)
    {
        if (oneBasedIndex is null) return null;

        int idx = oneBasedIndex.Value - 1;
        if (idx < 0 || idx >= lines.Count) return null;
        return lines[idx];
    }

    private static void AppendLineWithSourceTerminator(
        StringBuilder builder,
        char prefix,
        string text,
        LineSplitter.Line? source)
    {
        builder.Append(prefix).Append(text);

        if (source is { } src && src.IsLastWithoutTerminator)
        {
            builder.Append('\n');
            builder.Append(NoNewlineMarker);
        }
        else if (source is { } src2)
        {
            builder.Append(src2.Terminator);
        }
        else
        {
            // Source unknown — fall back to LF. Should not happen for well-formed hunks.
            builder.Append('\n');
        }
    }
}
