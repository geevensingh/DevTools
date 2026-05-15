using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiffViewer.Services;

/// <summary>
/// File-IO layer for <c>recents.json</c>. Exposes a single
/// <see cref="ReadAndMutateAsync"/> primitive that combines load + mutate
/// + persist under a kernel-level <see cref="FileShare.None"/> lock so
/// concurrent writers from multiple processes (e.g. several DiffViewer
/// windows launching at the same time) cannot lose each other's
/// contributions.
///
/// <para><b>Concurrency model</b>: opening with <see cref="FileShare.None"/>
/// causes the second writer to receive an <see cref="IOException"/>; we
/// retry briefly with exponential back-off and fail fast after the
/// configured timeout. The kernel guarantees only one writer is in the
/// critical section at a time, so the read-modify-write cycle is safe
/// against lost-update.</para>
///
/// <para>The file is rewritten in place (truncate + write) rather than
/// via temp+rename. Holding the exclusive handle until the rewrite
/// completes means partial-write recovery is the deserializer's job:
/// <see cref="RecentsJsonSerializer.Deserialize"/> returns
/// <see cref="RecentsDoc.Empty"/> on parse failure rather than throwing,
/// so a torn write degrades gracefully to "recents history lost" rather
/// than "app broken".</para>
/// </summary>
internal static class RecentsStore
{
    private static readonly TimeSpan DefaultRetryWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Read-only load. Used by the service at startup to populate the
    /// in-memory MRU. Returns <see cref="RecentsDoc.Empty"/> if the file
    /// doesn't exist, is empty, or fails to parse — never throws on
    /// "expected" missing-file situations.
    /// </summary>
    public static async Task<RecentsDoc> LoadAsync(string path, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path)) return RecentsDoc.Empty;

        try
        {
            // FileShare.Read so we don't block other readers; we're not
            // mutating in this path.
            using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            var json = await sr.ReadToEndAsync(ct).ConfigureAwait(false);
            return RecentsJsonSerializer.Deserialize(json);
        }
        catch
        {
            // Best-effort load: any IO failure or parse error degrades to
            // empty rather than crashing the app at startup.
            return RecentsDoc.Empty;
        }
    }

    /// <summary>
    /// Atomic read-modify-write under a <see cref="FileShare.None"/> lock.
    /// The <paramref name="mutate"/> delegate is invoked with the current
    /// on-disk state and must return the desired next state (a new
    /// <see cref="RecentsDoc"/>). Returns whatever
    /// <paramref name="mutate"/> produced.
    /// </summary>
    public static async Task<RecentsDoc> ReadAndMutateAsync(
        string path,
        Func<RecentsDoc, RecentsDoc> mutate,
        CancellationToken ct = default)
        => await ReadAndMutateAsync(path, mutate, DefaultRetryWindow, ct).ConfigureAwait(false);

    /// <summary>Overload exposed for tests so they can shorten the retry window.</summary>
    public static async Task<RecentsDoc> ReadAndMutateAsync(
        string path,
        Func<RecentsDoc, RecentsDoc> mutate,
        TimeSpan retryWindow,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(mutate);

        EnsureDirectoryExists(path);

        using var fs = await OpenWithRetryAsync(path, retryWindow, ct).ConfigureAwait(false);

        // Read whatever is currently on disk. An empty file (just-created
        // by FileMode.OpenOrCreate, or truncated by us mid-rewrite on a
        // prior crash) parses to Empty.
        var current = await ReadAllAsync(fs, ct).ConfigureAwait(false);
        var next = mutate(current) ?? RecentsDoc.Empty;

        // Truncate-and-rewrite. We still hold exclusive access (FileShare.None),
        // so no other process can read a half-written state.
        fs.Position = 0;
        fs.SetLength(0);
        var json = RecentsJsonSerializer.Serialize(next);
        var bytes = Encoding.UTF8.GetBytes(json);
        await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
        await fs.FlushAsync(ct).ConfigureAwait(false);

        return next;
    }

    private static async Task<RecentsDoc> ReadAllAsync(FileStream fs, CancellationToken ct)
    {
        if (fs.Length == 0) return RecentsDoc.Empty;
        fs.Position = 0;
        // ReadToEndAsync fully drains the stream; no leaving-open required
        // because the outer caller owns disposal of the underlying fs.
        using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        var json = await sr.ReadToEndAsync(ct).ConfigureAwait(false);
        return RecentsJsonSerializer.Deserialize(json);
    }

    private static async Task<FileStream> OpenWithRetryAsync(string path, TimeSpan retryWindow, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + retryWindow;
        var delay = InitialRetryDelay;

        while (true)
        {
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                // Another writer holds the FileShare.None lock; back off
                // briefly then retry. Cancellation is observed via the
                // Task.Delay below.
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaxRetryDelay.TotalMilliseconds));
            }
        }
    }

    private static void EnsureDirectoryExists(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
