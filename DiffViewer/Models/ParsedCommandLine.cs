namespace DiffViewer.Models;

/// <summary>
/// Parsed result of <c>DiffViewer</c>'s command line. The repo path is
/// always populated (current directory if not supplied); the two sides
/// describe what is being compared.
/// </summary>
public sealed record ParsedCommandLine(
    string RepoPath,
    DiffSide Left,
    DiffSide Right);

/// <summary>
/// What kind of failure happened while parsing or resolving the command line.
/// </summary>
public enum CommandLineErrorKind
{
    /// <summary>Successful parse — there is no error.</summary>
    None,

    /// <summary>More positional arguments than the grammar allows.</summary>
    TooManyArguments,

    /// <summary>An argument that looks like a path doesn't exist.</summary>
    PathDoesNotExist,

    /// <summary>An argument that looks like a path is not a git repository.</summary>
    NotAGitRepository,

    /// <summary>An argument that doesn't look like a path can't be resolved as a commit-ish.</summary>
    UnknownCommitIsh,

    /// <summary>Unsupported flag or unknown switch.</summary>
    UnknownFlag,
}

/// <summary>
/// Failure detail for a parse that did not produce a <see cref="ParsedCommandLine"/>.
/// </summary>
public sealed record CommandLineError(
    CommandLineErrorKind Kind,
    string Message);

/// <summary>
/// Discriminated result of <see cref="DiffViewer.Services.ICommandLineParser.Parse"/>.
/// Exactly one of the two properties is non-null.
/// </summary>
public sealed record CommandLineParseResult(
    ParsedCommandLine? Parsed,
    CommandLineError? Error)
{
    public bool IsSuccess => Parsed is not null;

    public static CommandLineParseResult Success(ParsedCommandLine parsed) =>
        new(parsed, null);

    public static CommandLineParseResult Failure(CommandLineErrorKind kind, string message) =>
        new(null, new CommandLineError(kind, message));
}
