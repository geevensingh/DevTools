namespace DiffViewer.Models;

/// <summary>
/// One line in a diff hunk. <see cref="OldLineNumber"/> / <see cref="NewLineNumber"/>
/// are 1-based line numbers in the original buffers; either is <c>null</c> for
/// pure inserts/deletes respectively.
/// </summary>
public sealed record DiffLine(
    DiffLineKind Kind,
    int? OldLineNumber,
    int? NewLineNumber,
    string Text);

public enum DiffLineKind
{
    Context,
    Inserted,
    Deleted,
    /// <summary>Line that exists on both sides but with intra-line modifications.</summary>
    Modified,
}
