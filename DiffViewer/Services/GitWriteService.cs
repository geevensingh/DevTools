using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Utility;

namespace DiffViewer.Services;

/// <summary>
/// Default <see cref="IGitWriteService"/>. Shells out to <c>git.exe</c>
/// (resolved via PATH; override via the <c>gitExePath</c> ctor param for
/// tests or non-standard installs) for every operation, raising
/// <see cref="BeforeOperation"/> + <see cref="AfterOperation"/> so
/// callers can suspend the repo watcher and refresh the LibGit2Sharp
/// index cache.
/// </summary>
public sealed class GitWriteService : IGitWriteService
{
    private readonly string _gitExe;

    public GitWriteService(string gitExePath = "git")
    {
        _gitExe = gitExePath ?? throw new ArgumentNullException(nameof(gitExePath));
    }

    public event EventHandler<GitWriteOperationEventArgs>? BeforeOperation;
    public event EventHandler<GitWriteOperationEventArgs>? AfterOperation;

    // ---------------- Hunk operations ----------------

    public Task<GitWriteResult> StageHunkAsync(string repoPath, HunkPatchInputs inputs, CancellationToken ct = default)
        => RunHunkApplyAsync(repoPath, inputs, GitWriteOperationKind.StageHunk,
            new[] { "apply", "--cached", "--recount", "--whitespace=nowarn" }, ct);

    public Task<GitWriteResult> UnstageHunkAsync(string repoPath, HunkPatchInputs inputs, CancellationToken ct = default)
        => RunHunkApplyAsync(repoPath, inputs, GitWriteOperationKind.UnstageHunk,
            new[] { "apply", "--cached", "--reverse", "--recount", "--whitespace=nowarn" }, ct);

    public Task<GitWriteResult> RevertHunkAsync(string repoPath, HunkPatchInputs inputs, CancellationToken ct = default)
        => RunHunkApplyAsync(repoPath, inputs, GitWriteOperationKind.RevertHunk,
            new[] { "apply", "--reverse", "--recount", "--whitespace=nowarn" }, ct);

    private async Task<GitWriteResult> RunHunkApplyAsync(
        string repoPath,
        HunkPatchInputs inputs,
        GitWriteOperationKind kind,
        IReadOnlyList<string> applyArgs,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repoPath);
        ArgumentNullException.ThrowIfNull(inputs);

        var patch = UnifiedDiffFormatter.Format(
            oldPath: inputs.FilePath,
            newPath: inputs.FilePath,
            hunks: new[] { inputs.Hunk },
            leftSource: inputs.LeftSource,
            rightSource: inputs.RightSource);

        // Write to a temp file rather than piping through stdin to avoid
        // any encoding-conversion surprises in the Process stream wiring.
        var patchFile = Path.Combine(Path.GetTempPath(), $"diffviewer-{Guid.NewGuid():N}.patch");
        // UTF-8 without BOM, no extra newline added.
        await File.WriteAllBytesAsync(patchFile, Encoding.UTF8.GetBytes(patch), ct).ConfigureAwait(false);

        try
        {
            var args = new List<string>(applyArgs.Count + 1);
            args.AddRange(applyArgs);
            args.Add(patchFile);

            return await RunGitAsync(repoPath, kind, inputs.FilePath, args, ct).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(patchFile); } catch { /* best-effort cleanup */ }
        }
    }

    // ---------------- Whole-file operations ----------------

    public Task<GitWriteResult> StageFileAsync(string repoPath, string filePath, CancellationToken ct = default)
        => RunGitAsync(repoPath, GitWriteOperationKind.StageFile, filePath,
            new[] { "add", "--", filePath }, ct);

    public Task<GitWriteResult> UnstageFileAsync(string repoPath, string filePath, CancellationToken ct = default)
        => RunGitAsync(repoPath, GitWriteOperationKind.UnstageFile, filePath,
            new[] { "restore", "--staged", "--", filePath }, ct);

    // ---------------- .gitignore append ----------------

    public Task<GitWriteResult> AddToGitignoreAsync(string repoPath, string repoRelativePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(repoPath);
        ArgumentNullException.ThrowIfNull(repoRelativePath);

        var operationId = Guid.NewGuid();
        BeforeOperation?.Invoke(this, new GitWriteOperationEventArgs
        {
            OperationId = operationId,
            Kind = GitWriteOperationKind.AddToGitignore,
            FilePath = repoRelativePath,
        });

        GitWriteResult result;
        try
        {
            var pattern = BuildGitignorePattern(repoRelativePath);
            var gitignorePath = Path.Combine(repoPath, ".gitignore");

            string existing = File.Exists(gitignorePath)
                ? File.ReadAllText(gitignorePath)
                : string.Empty;

            // Idempotent: bail if the exact pattern is already present.
            var existingLines = existing.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            if (existingLines.Any(l => l == pattern))
            {
                result = GitWriteResult.Ok($"already in .gitignore: {pattern}");
            }
            else
            {
                var sb = new StringBuilder(existing.Length + pattern.Length + 2);
                sb.Append(existing);
                if (existing.Length > 0 && !existing.EndsWith("\n", StringComparison.Ordinal))
                {
                    sb.Append('\n');
                }
                sb.Append(pattern).Append('\n');

                WriteAtomicLf(gitignorePath, sb.ToString());
                result = GitWriteResult.Ok($"added to .gitignore: {pattern}");
            }
        }
        catch (Exception ex)
        {
            result = GitWriteResult.Fail(-1, ex.Message);
        }

        AfterOperation?.Invoke(this, new GitWriteOperationEventArgs
        {
            OperationId = operationId,
            Kind = GitWriteOperationKind.AddToGitignore,
            FilePath = repoRelativePath,
            Result = result,
        });
        return Task.FromResult(result);
    }

    /// <summary>
    /// Build a root-anchored, gitignore-escaped pattern from a
    /// repo-relative path. See the plan's
    /// "<c>.gitignore</c> append protocol" for the rule list.
    /// </summary>
    internal static string BuildGitignorePattern(string repoRelativePath)
    {
        // 1. Forward slashes (gitignore is platform-agnostic).
        var path = repoRelativePath.Replace('\\', '/');

        // 2. Escape gitignore-significant characters.
        var sb = new StringBuilder(path.Length + 4);
        foreach (var c in path)
        {
            if (c == '!' || c == '#' || c == '[' || c == ']' || c == '*' || c == '?' || c == '\\')
            {
                sb.Append('\\');
            }
            sb.Append(c);
        }

        // 2b. Escape trailing whitespace one char at a time.
        var s = sb.ToString();
        var trailingWs = 0;
        for (int i = s.Length - 1; i >= 0 && (s[i] == ' ' || s[i] == '\t'); i--) trailingWs++;
        if (trailingWs > 0)
        {
            var stem = s[..^trailingWs];
            var tail = s[^trailingWs..];
            var escaped = new StringBuilder(stem);
            foreach (var c in tail) escaped.Append('\\').Append(c);
            s = escaped.ToString();
        }

        // 3. Anchor to repo root.
        if (!s.StartsWith('/')) s = "/" + s;

        return s;
    }

    private static void WriteAtomicLf(string finalPath, string content)
    {
        var dir = Path.GetDirectoryName(finalPath)!;
        var tempPath = Path.Combine(dir, Path.GetFileName(finalPath) + ".tmp");
        // Force LF terminators regardless of OS — git's universal convention.
        File.WriteAllBytes(tempPath, Encoding.UTF8.GetBytes(content));

        if (File.Exists(finalPath))
        {
            File.Replace(tempPath, finalPath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, finalPath);
        }
    }

    // ---------------- Recycle Bin delete (Win32) ----------------

    public Task<GitWriteResult> DeleteToRecycleBinAsync(string repoPath, string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var operationId = Guid.NewGuid();
        BeforeOperation?.Invoke(this, new GitWriteOperationEventArgs
        {
            OperationId = operationId,
            Kind = GitWriteOperationKind.DeleteToRecycleBin,
            FilePath = filePath,
        });

        GitWriteResult result;
        try
        {
            var absolute = Path.IsPathRooted(filePath)
                ? filePath
                : Path.Combine(repoPath, filePath);

            // SHFileOperation requires double-null-terminated path string.
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = absolute + "\0\0",
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
            };
            int rc = SHFileOperationW(ref op);
            if (rc != 0)
            {
                result = GitWriteResult.Fail(rc, $"SHFileOperation failed (code {rc}) deleting {absolute}");
            }
            else if (op.fAnyOperationsAborted)
            {
                result = GitWriteResult.Fail(-1, $"Recycle-bin delete aborted for {absolute}");
            }
            else
            {
                result = GitWriteResult.Ok($"recycled {absolute}");
            }
        }
        catch (Exception ex)
        {
            result = GitWriteResult.Fail(-1, ex.Message);
        }

        AfterOperation?.Invoke(this, new GitWriteOperationEventArgs
        {
            OperationId = operationId,
            Kind = GitWriteOperationKind.DeleteToRecycleBin,
            FilePath = filePath,
            Result = result,
        });
        return Task.FromResult(result);
    }

    // ---------------- Process plumbing ----------------

    private async Task<GitWriteResult> RunGitAsync(
        string repoPath,
        GitWriteOperationKind kind,
        string filePath,
        IReadOnlyList<string> args,
        CancellationToken ct)
    {
        var operationId = Guid.NewGuid();
        BeforeOperation?.Invoke(this, new GitWriteOperationEventArgs
        {
            OperationId = operationId,
            Kind = kind,
            FilePath = filePath,
        });

        GitWriteResult result;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _gitExe,
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            // Force git to operate inside the requested repo even if
            // someone passes a non-canonical CWD. -C runs first so the
            // remaining args see the right repo.
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(repoPath);
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start {_gitExe}");

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            result = proc.ExitCode == 0
                ? GitWriteResult.Ok(stdout)
                : GitWriteResult.Fail(proc.ExitCode, stderr, stdout);
        }
        catch (Exception ex)
        {
            result = GitWriteResult.Fail(-1, ex.Message);
        }

        AfterOperation?.Invoke(this, new GitWriteOperationEventArgs
        {
            OperationId = operationId,
            Kind = kind,
            FilePath = filePath,
            Result = result,
        });
        return result;
    }

    // ---------------- Win32 ----------------

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT lpFileOp);

    // SHFILEOPSTRUCTW with default packing - Pack=1 caused AccessViolation
    // on 64-bit because the runtime mis-aligned the pointer fields.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
}
