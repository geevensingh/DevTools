namespace ConnectionsSolver;

public static class BigramResolver
{
    private const string DefaultFileName = "count_2w.txt";

    public const string LookupOrderDescription =
        "Lookup order: (1) --bigrams <path>, (2) BIGRAMS_PATH env var, " +
        "(3) <Downloads>\\count_2w.txt.";

    /// <summary>
    /// Resolves the path to a Norvig-format bigram counts text file
    /// (https://norvig.com/ngrams/count_2w.txt). Same fallback pattern as
    /// <see cref="EmbeddingsResolver"/>: explicit path / env var wins outright;
    /// otherwise looks in the user's Downloads folder.
    /// </summary>
    /// <returns>The resolved path, or null if no fallback produced a candidate.</returns>
    public static string? Resolve(string? explicitPath, Action<string>? log = null)
    {
        log ??= _ => { };

        if (!string.IsNullOrEmpty(explicitPath))
        {
            log($"Using --bigrams: {explicitPath}");
            return explicitPath;
        }

        var env = Environment.GetEnvironmentVariable("BIGRAMS_PATH");
        if (!string.IsNullOrEmpty(env))
        {
            log($"Using BIGRAMS_PATH: {env}");
            return env;
        }

        var downloads = GetDownloadsFolder();
        if (downloads == null)
        {
            log("Downloads folder not found; no fallback available.");
            return null;
        }

        var candidate = Path.Combine(downloads, DefaultFileName);
        if (File.Exists(candidate))
        {
            log($"Using existing file: {candidate}");
            return candidate;
        }
        log($"Not found: {candidate}");

        return null;
    }

    private static string? GetDownloadsFolder()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile)) return null;
        var dl = Path.Combine(userProfile, "Downloads");
        return Directory.Exists(dl) ? dl : null;
    }
}
