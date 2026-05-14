using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Default <see cref="ICommandLineParser"/>. Implements the disambiguation
/// table from the plan:
/// <list type="bullet">
///   <item><c>(no args)</c>            → working tree vs HEAD in cwd</item>
///   <item><c>repoPath</c>             → working tree vs HEAD in repoPath</item>
///   <item><c>commit</c>               → working tree vs commit in cwd</item>
///   <item><c>repoPath commit</c>      → working tree vs commit in repoPath</item>
///   <item><c>commitA commitB</c>      → commitA vs commitB in cwd</item>
///   <item><c>repoPath commitA commitB</c> → commitA vs commitB in repoPath</item>
/// </list>
/// Disambiguation: an argument is treated as a repo path iff
/// <see cref="ICommandLineEnvironment.PathExists"/> AND
/// <see cref="ICommandLineEnvironment.IsGitRepository"/>; otherwise it is
/// resolved as a commit-ish.
///
/// <para>When the resolved repo path turns out to be a subdirectory of a
/// repo (or the current working directory is one), the parser falls back to
/// <see cref="ICommandLineEnvironment.TryDiscoverRepoRoot"/> so the app can
/// be launched from anywhere inside a worktree and still load the whole
/// repo.</para>
/// </summary>
public sealed class CommandLineParser : ICommandLineParser
{
    public CommandLineParseResult Parse(IReadOnlyList<string> args, ICommandLineEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);

        // Reject unknown switches early — every arg starting with "-" is a flag we don't (yet) support.
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].Length > 0 && args[i][0] == '-')
            {
                return CommandLineParseResult.Failure(
                    CommandLineErrorKind.UnknownFlag,
                    $"Unknown flag: {args[i]}");
            }
        }

        if (args.Count > 3)
        {
            return CommandLineParseResult.Failure(
                CommandLineErrorKind.TooManyArguments,
                $"Too many arguments: expected 0-3, got {args.Count}");
        }

        // Decide whether the first arg is a repo path. We probe filesystem first
        // (the plan: "checking the path on disk first, then falling back to commit-ish").
        string repoPath = env.CurrentDirectory;
        int sideArgsStart = 0;

        if (args.Count > 0 && LooksLikeRepoPath(args[0], env))
        {
            repoPath = args[0];
            sideArgsStart = 1;
        }
        else if (args.Count > 0 && args[0].Length > 0 && IsLikelyPath(args[0]))
        {
            // Argument looks like a path (contains a separator, drive letter, leading dot)
            // but doesn't exist or isn't a repo on its own. We still allow it iff it's a
            // subdirectory of a repo — discovery resolves that. If the path doesn't even
            // exist, fail loudly; "..\foo" really meant a path, not a ref.
            if (!env.PathExists(args[0]))
            {
                return CommandLineParseResult.Failure(
                    CommandLineErrorKind.PathDoesNotExist,
                    $"Path does not exist: {args[0]}");
            }

            repoPath = args[0];
            sideArgsStart = 1;
        }

        // Make sure the resolved repo path is, or sits inside, a git repo. The
        // discovery fallback handles "launched from a subdirectory of a repo".
        if (!env.IsGitRepository(repoPath))
        {
            var discovered = env.TryDiscoverRepoRoot(repoPath);
            if (discovered is null)
            {
                return CommandLineParseResult.Failure(
                    CommandLineErrorKind.NotAGitRepository,
                    $"Not a git repository: {repoPath}");
            }

            repoPath = discovered;
        }

        int sideCount = args.Count - sideArgsStart;
        DiffSide left;
        DiffSide right;

        switch (sideCount)
        {
            case 0:
                // Working tree vs HEAD.
                left = new DiffSide.CommitIsh("HEAD");
                right = new DiffSide.WorkingTree();
                break;

            case 1:
            {
                string commit = args[sideArgsStart];
                if (!env.TryResolveCommitIsh(repoPath, commit))
                {
                    return CommandLineParseResult.Failure(
                        CommandLineErrorKind.UnknownCommitIsh,
                        $"Cannot resolve `{commit}` in repo {repoPath}");
                }

                left = new DiffSide.CommitIsh(commit);
                right = new DiffSide.WorkingTree();
                break;
            }

            case 2:
            {
                string commitA = args[sideArgsStart];
                string commitB = args[sideArgsStart + 1];

                if (!env.TryResolveCommitIsh(repoPath, commitA))
                {
                    return CommandLineParseResult.Failure(
                        CommandLineErrorKind.UnknownCommitIsh,
                        $"Cannot resolve `{commitA}` in repo {repoPath}");
                }

                if (!env.TryResolveCommitIsh(repoPath, commitB))
                {
                    return CommandLineParseResult.Failure(
                        CommandLineErrorKind.UnknownCommitIsh,
                        $"Cannot resolve `{commitB}` in repo {repoPath}");
                }

                left = new DiffSide.CommitIsh(commitA);
                right = new DiffSide.CommitIsh(commitB);
                break;
            }

            default:
                // Unreachable — guarded above by the 0–3 arg cap.
                return CommandLineParseResult.Failure(
                    CommandLineErrorKind.TooManyArguments,
                    $"Too many side arguments: {sideCount}");
        }

        return CommandLineParseResult.Success(new ParsedCommandLine(repoPath, left, right));
    }

    private static bool LooksLikeRepoPath(string arg, ICommandLineEnvironment env) =>
        env.PathExists(arg) && env.IsGitRepository(arg);

    /// <summary>
    /// True if the argument <em>looks</em> like a filesystem path (rather than a commit-ish).
    /// We err on the side of treating it as a commit-ish (more permissive) — only
    /// flag obvious path-like inputs to give a clearer error message.
    /// </summary>
    private static bool IsLikelyPath(string arg)
    {
        // Drive-letter paths: "C:\..." or "C:/..."
        if (arg.Length >= 3 && char.IsLetter(arg[0]) && arg[1] == ':' &&
            (arg[2] == '\\' || arg[2] == '/'))
        {
            return true;
        }

        // UNC paths.
        if (arg.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        // Relative paths starting with "." or ".."
        if (arg.StartsWith("./", StringComparison.Ordinal) ||
            arg.StartsWith(".\\", StringComparison.Ordinal) ||
            arg.StartsWith("../", StringComparison.Ordinal) ||
            arg.StartsWith("..\\", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
