using System.IO.Compression;

namespace ConnectionsSolver;

public static class EmbeddingsResolver
{
    private const string DefaultFileName = "glove.6B.300d.txt";
    private const string DefaultSubfolder = "glove.6B";
    private const string DefaultZipName = "glove.6B.zip";

    public const string LookupOrderDescription =
        "Lookup order: (1) --embeddings <path>, (2) GLOVE_PATH env var, " +
        "(3) <Downloads>\\glove.6B\\glove.6B.300d.txt, " +
        "(4) <Downloads>\\glove.6B.zip (auto-extracts glove.6B.300d.txt into <Downloads>\\glove.6B\\).";

    /// <summary>
    /// Resolves the path to a GloVe text file. An explicit path or GLOVE_PATH wins
    /// outright (no fallback if those point at a missing file — that's user error and
    /// should surface as an error rather than be silently overridden). If neither is set,
    /// looks in the user's Downloads folder for either the extracted file or the zip.
    /// </summary>
    /// <returns>The resolved path, or null if no fallback produced a candidate.</returns>
    public static string? Resolve(string? explicitPath, Action<string>? log = null)
    {
        log ??= _ => { };

        if (!string.IsNullOrEmpty(explicitPath))
        {
            log($"Using --embeddings: {explicitPath}");
            return explicitPath;
        }

        var env = Environment.GetEnvironmentVariable("GLOVE_PATH");
        if (!string.IsNullOrEmpty(env))
        {
            log($"Using GLOVE_PATH: {env}");
            return env;
        }

        var downloads = GetDownloadsFolder();
        if (downloads == null)
        {
            log("Downloads folder not found; no fallback available.");
            return null;
        }

        var extracted = Path.Combine(downloads, DefaultSubfolder, DefaultFileName);
        if (File.Exists(extracted))
        {
            log($"Using existing extracted file: {extracted}");
            return extracted;
        }
        log($"Not found: {extracted}");

        var zip = Path.Combine(downloads, DefaultZipName);
        if (File.Exists(zip))
        {
            log($"Found zip: {zip}");
            var targetDir = Path.Combine(downloads, DefaultSubfolder);
            ExtractSingleFile(zip, DefaultFileName, targetDir, log);
            return Path.Combine(targetDir, DefaultFileName);
        }
        log($"Not found: {zip}");

        return null;
    }

    private static string? GetDownloadsFolder()
    {
        // The conventional default on Windows. If the user has redirected their Downloads
        // folder somewhere unusual (rare), they should use --embeddings or GLOVE_PATH.
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile)) return null;
        var dl = Path.Combine(userProfile, "Downloads");
        return Directory.Exists(dl) ? dl : null;
    }

    private static void ExtractSingleFile(string zipPath, string entryName, string targetDir, Action<string> log)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(
            e => string.Equals(e.Name, entryName, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            throw new InvalidDataException($"'{entryName}' not found inside {zipPath}");

        Directory.CreateDirectory(targetDir);
        var destPath = Path.Combine(targetDir, entry.Name);
        var tmpPath = destPath + ".tmp";

        if (File.Exists(destPath))
        {
            log($"Already extracted: {destPath}");
            return;
        }
        if (File.Exists(tmpPath)) File.Delete(tmpPath);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        log($"Extracting {entry.Name} ({entry.Length / 1024 / 1024} MB) -> {destPath}");
        try
        {
            entry.ExtractToFile(tmpPath, overwrite: true);
            File.Move(tmpPath, destPath);
        }
        catch
        {
            if (File.Exists(tmpPath))
            {
                try { File.Delete(tmpPath); } catch { /* best effort */ }
            }
            throw;
        }
        sw.Stop();
        log($"Extracted in {sw.Elapsed.TotalSeconds:F1}s");
    }
}
