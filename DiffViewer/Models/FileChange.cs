namespace DiffViewer.Models;

/// <summary>
/// One row in the left-pane file list. Carries enough information to
/// render the row, drive the right-pane diff, and (if the row is in a
/// write-eligible layer) build a patch via <see cref="DiffViewer.Services.IGitWriteService"/>.
/// </summary>
/// <param name="Path">Repo-relative path with forward slashes (the new path for renames).</param>
/// <param name="OldPath">Pre-rename repo-relative path; <c>null</c> unless <see cref="Status"/> is <see cref="FileStatus.Renamed"/>.</param>
/// <param name="Status">Porcelain-style status code.</param>
/// <param name="ConflictCode">Two-letter conflict code (UU, AA, DU, UD, DD, AU, UA); <c>null</c> unless <see cref="Status"/> is <see cref="FileStatus.Conflicted"/>.</param>
/// <param name="Layer">Which working-tree layer this row lives in.</param>
/// <param name="LeftBlobSha">SHA of the left-side blob (<c>null</c> if the file is added on this side).</param>
/// <param name="RightBlobSha">SHA of the right-side blob (<c>null</c> if the file is deleted on this side, or the right side is the working tree).</param>
/// <param name="IsBinary">True if the file is detected as binary (via <c>.gitattributes</c> or NUL-byte heuristic).</param>
/// <param name="LeftFileSizeBytes">Size of the left blob in bytes (<c>null</c> if unknown / not yet probed).</param>
/// <param name="RightFileSizeBytes">Size of the right blob/file in bytes (<c>null</c> if unknown / not yet probed).</param>
/// <param name="IsLfsPointer">True if either side is an unfetched Git LFS pointer.</param>
/// <param name="IsSparseNotCheckedOut">True if the file is in the sparse set but missing from disk.</param>
/// <param name="OldMode">Pre-change file mode (e.g. <c>0644</c>); 0 if not applicable.</param>
/// <param name="NewMode">Post-change file mode; 0 if not applicable.</param>
public sealed record FileChange(
    string Path,
    string? OldPath,
    FileStatus Status,
    string? ConflictCode,
    WorkingTreeLayer Layer,
    string? LeftBlobSha,
    string? RightBlobSha,
    bool IsBinary,
    long? LeftFileSizeBytes,
    long? RightFileSizeBytes,
    bool IsLfsPointer,
    bool IsSparseNotCheckedOut,
    int OldMode,
    int NewMode)
{
    /// <summary>True if the change is purely a file-mode flip (e.g. +x).</summary>
    public bool IsModeOnlyChange =>
        OldMode != 0 && NewMode != 0 && OldMode != NewMode &&
        LeftBlobSha is not null && RightBlobSha is not null &&
        LeftBlobSha == RightBlobSha;

    /// <summary>True if the file's left and right paths differ (rename or copy).</summary>
    public bool IsRenameOrCopy =>
        Status is FileStatus.Renamed or FileStatus.Copied &&
        OldPath is not null && OldPath != Path;
}
