using System.Text;
using DiffViewer.Models;

namespace DiffViewer.Rendering;

/// <summary>
/// Builds the inline-mode document: a single text buffer where each hunk
/// is preceded by a <c>@@ -OldStart,OldLen +NewStart,NewLen @@</c> header
/// and lines are prefixed with <c> </c> (context), <c>-</c> (deleted) or
/// <c>+</c> (inserted). Returns both the rendered text and a per-line
/// classification for the renderer to tint.
/// </summary>
public static class InlineDiffBuilder
{
    public sealed record InlineDocument(
        string Text,
        IReadOnlyDictionary<int, DiffLineKind> LineKinds);

    public static InlineDocument Build(IReadOnlyList<DiffHunk> hunks)
    {
        if (hunks.Count == 0)
        {
            return new InlineDocument(string.Empty, new Dictionary<int, DiffLineKind>());
        }

        var sb = new StringBuilder();
        var lineKinds = new Dictionary<int, DiffLineKind>();
        int currentLine = 1;

        for (int h = 0; h < hunks.Count; h++)
        {
            var hunk = hunks[h];

            // Hunk header line - rendered without a tint so the renderer
            // skips it (no entry in lineKinds).
            sb.Append("@@ -")
              .Append(hunk.OldStartLine).Append(',').Append(hunk.OldLineCount)
              .Append(" +")
              .Append(hunk.NewStartLine).Append(',').Append(hunk.NewLineCount)
              .Append(" @@");
            if (hunk.FunctionContext is { Length: > 0 })
            {
                sb.Append(' ').Append(hunk.FunctionContext);
            }
            sb.Append('\n');
            currentLine++;

            foreach (var line in hunk.Lines)
            {
                char prefix = line.Kind switch
                {
                    DiffLineKind.Inserted => '+',
                    DiffLineKind.Deleted => '-',
                    DiffLineKind.Modified => '~', // unused in current builder
                    _ => ' ',
                };

                sb.Append(prefix).Append(line.Text).Append('\n');
                if (line.Kind != DiffLineKind.Context)
                {
                    lineKinds[currentLine] = line.Kind;
                }
                currentLine++;
            }

            // Blank separator between hunks (skip after the last).
            if (h < hunks.Count - 1)
            {
                sb.Append('\n');
                currentLine++;
            }
        }

        return new InlineDocument(sb.ToString(), lineKinds);
    }
}
