using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// JSON-backed <see cref="ISettingsService"/>. Reads from
/// <c>%APPDATA%\DiffViewer\settings.json</c>; every write goes through a
/// temp-file + <see cref="File.Replace(string, string, string?)"/> atomic
/// swap so a crash mid-write or a concurrent hand-edit can never leave
/// the file in a half-written state.
///
/// <para>Corrupt or future-version files are renamed to
/// <c>settings.json.bak.&lt;unix-time&gt;</c> and defaults are loaded;
/// the UI surfaces this via <see cref="LastLoadOutcome"/>.</para>
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly string _filePath;
    private readonly object _ioLock = new();
    private AppSettings _current = new();

    /// <summary>Default file path under <c>%APPDATA%\DiffViewer\</c>.</summary>
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DiffViewer",
        "settings.json");

    public AppSettings Current => _current;
    public SettingsLoadOutcome LastLoadOutcome { get; private set; } = SettingsLoadOutcome.DefaultsUsed;
    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public SettingsService() : this(DefaultFilePath) { }

    public SettingsService(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        LoadFromDisk();
    }

    public void Save(AppSettings updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        var stamped = updated with { SchemaVersion = AppSettings.CurrentSchemaVersion };
        AppSettings previous;

        lock (_ioLock)
        {
            EnsureDirectoryExists();
            WriteAtomic(stamped);
            previous = _current;
            _current = stamped;
        }

        Changed?.Invoke(this, new SettingsChangedEventArgs(previous, stamped));
    }

    public AppSettings Update(Func<AppSettings, AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        var next = mutate(_current);
        Save(next);
        return _current;
    }

    // ---- internals ----

    private void LoadFromDisk()
    {
        if (!File.Exists(_filePath))
        {
            _current = new AppSettings();
            LastLoadOutcome = SettingsLoadOutcome.DefaultsUsed;
            return;
        }

        string text;
        try
        {
            text = File.ReadAllText(_filePath);
        }
        catch
        {
            // Couldn't read at all (permissions, locked) - back up + defaults.
            BackupAndUseDefaults(SettingsLoadOutcome.CorruptBackedUp);
            return;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            BackupAndUseDefaults(SettingsLoadOutcome.CorruptBackedUp);
            return;
        }

        if (root is not JsonObject obj)
        {
            BackupAndUseDefaults(SettingsLoadOutcome.CorruptBackedUp);
            return;
        }

        int version = obj["schemaVersion"]?.GetValue<int?>() ?? 0;

        if (version > AppSettings.CurrentSchemaVersion)
        {
            // User downgraded DiffViewer over a newer schema - don't try to read.
            BackupAndUseDefaults(SettingsLoadOutcome.FutureVersionBackedUp);
            return;
        }

        bool migrated = false;
        if (version < AppSettings.CurrentSchemaVersion)
        {
            try
            {
                obj = SettingsMigrations.MigrateUpTo(obj, version, AppSettings.CurrentSchemaVersion);
                migrated = true;
            }
            catch
            {
                BackupAndUseDefaults(SettingsLoadOutcome.CorruptBackedUp);
                return;
            }
        }

        try
        {
            _current = SettingsJsonSerializer.Deserialize(obj);
            LastLoadOutcome = migrated ? SettingsLoadOutcome.Migrated : SettingsLoadOutcome.Loaded;
        }
        catch
        {
            BackupAndUseDefaults(SettingsLoadOutcome.CorruptBackedUp);
        }
    }

    private void BackupAndUseDefaults(SettingsLoadOutcome reason)
    {
        try
        {
            var backupPath = $"{_filePath}.bak.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            // Replace any existing backup with that name (rare but possible).
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Move(_filePath, backupPath);
        }
        catch
        {
            // If we can't move it, leave the bad file in place; we still load defaults.
        }
        _current = new AppSettings();
        LastLoadOutcome = reason;
    }

    private void EnsureDirectoryExists()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private void WriteAtomic(AppSettings settings)
    {
        var tmpPath = $"{_filePath}.tmp";
        var json = SettingsJsonSerializer.Serialize(settings);
        File.WriteAllText(tmpPath, json);
        if (File.Exists(_filePath))
        {
            // File.Replace is atomic on NTFS; falls back to delete+move on
            // exotic filesystems (acceptable - the temp file is still safe
            // until the original is gone).
            File.Replace(tmpPath, _filePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tmpPath, _filePath);
        }
    }
}
