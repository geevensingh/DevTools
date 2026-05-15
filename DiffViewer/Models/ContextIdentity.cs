using System;
using System.IO;

namespace DiffViewer.Models;

/// <summary>
/// Canonical identity used to dedup recent launch contexts. Two contexts
/// with the same <see cref="CanonicalRepoPath"/> and the same pair of
/// <see cref="DiffSide"/> values represent "the same diff" even if the
/// user typed the path or refs differently.
///
/// <para><b>Path canonicalization</b>: lowercase comparison via
/// <see cref="StringComparison.OrdinalIgnoreCase"/> (Windows file system
/// is case-insensitive); trailing directory separators are trimmed; the
/// path is run through <see cref="Path.GetFullPath"/> to resolve <c>..</c>
/// segments and Windows long-path / short-path differences. <see cref="DiffSide"/>
/// equality is record-based and case-sensitive on commit-ish refs (Git
/// itself treats <c>HEAD</c> and <c>head</c> differently in some refspecs;
/// a literal-string match is the safer dedup rule).</para>
///
/// <para>Display sides (the user's raw input) are kept separately on
/// <see cref="RecentLaunchContext"/> so the UI can render exactly what
/// was typed. Dedup happens against this canonical struct only.</para>
/// </summary>
public readonly record struct ContextIdentity(
    string CanonicalRepoPath,
    DiffSide Left,
    DiffSide Right);

/// <summary>
/// Factory + canonicalization helpers for <see cref="ContextIdentity"/>.
/// Kept separate from the struct so the struct itself stays a thin
/// equality target with no IO.
/// </summary>
public static class ContextIdentityFactory
{
    /// <summary>
    /// Build a canonical <see cref="ContextIdentity"/> from a raw repo
    /// path + sides. The repo path is normalised via
    /// <see cref="CanonicalizeRepoPath"/>; the sides are passed through
    /// untouched (they're already structured records).
    /// </summary>
    public static ContextIdentity Create(string repoPath, DiffSide left, DiffSide right)
    {
        ArgumentNullException.ThrowIfNull(repoPath);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new ContextIdentity(CanonicalizeRepoPath(repoPath), left, right);
    }

    /// <summary>
    /// Canonical form of a repo path used for dedup. <see cref="Path.GetFullPath"/>
    /// resolves relative segments and short-path / long-path differences;
    /// trailing directory separators are stripped so <c>C:\Foo\</c> and
    /// <c>C:\Foo</c> compare equal.
    /// </summary>
    public static string CanonicalizeRepoPath(string repoPath)
    {
        ArgumentNullException.ThrowIfNull(repoPath);
        if (repoPath.Length == 0) return repoPath;
        var full = Path.GetFullPath(repoPath);
        return Path.TrimEndingDirectorySeparator(full);
    }

    /// <summary>
    /// True when two raw repo path strings refer to the same on-disk
    /// directory (after canonicalisation + case-insensitive compare).
    /// </summary>
    public static bool RepoPathsEqual(string a, string b) =>
        string.Equals(CanonicalizeRepoPath(a), CanonicalizeRepoPath(b), StringComparison.OrdinalIgnoreCase);
}
