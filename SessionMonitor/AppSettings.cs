using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotSessionMonitor;

/// <summary>
/// User-visible app settings. Persisted as JSON to
/// <c>%LOCALAPPDATA%\CopilotSessionMonitor\settings.json</c>. Saves are
/// debounced and tolerated to fail silently — a corrupt or unwritable
/// settings file is non-fatal; we fall back to defaults.
/// </summary>
public sealed class AppSettings
{
    public double WindowWidth { get; set; } = 540;
    public double WindowHeight { get; set; } = 640;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool IsPinned { get; set; } = true;
    public bool ShowOffline { get; set; }
    public bool GroupByRepo { get; set; }

    /// <summary>
    /// How long an alive process can go without writing a single event before
    /// we treat it as abandoned and reclassify it as Offline. Stored as an
    /// integer-hour value because that's the granularity the settings UI
    /// exposes; runtime <see cref="Core.SessionStateMachine.StaleThreshold"/>
    /// is hydrated from this.
    /// </summary>
    public int StaleThresholdHours { get; set; } = 4;

    /// <summary>Heartbeat tick interval. Lower values pick up changes faster but
    /// cost more CPU and disk. The events.jsonl FileSystemWatcher catches most
    /// real-time work regardless of this value; the heartbeat is mostly for
    /// PID liveness, git status, and stale-detection.</summary>
    public int HeartbeatSeconds { get; set; } = 5;

    public bool NotifyOnBlue { get; set; } = true;
    public bool NotifyOnRedToGreen { get; set; } = true;

    /// <summary>Session IDs the user has muted from notifications. Survives restart.</summary>
    public HashSet<string> MutedSessionIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// System sound to play when a Blue (waiting on you) toast fires.
    /// One of: <c>None, Default, Asterisk, Beep, Question, Exclamation, Hand</c>.
    /// Defaults to <c>Asterisk</c> — distinct, attention-grabbing.
    /// </summary>
    public string BlueSound { get; set; } = "Asterisk";

    /// <summary>
    /// System sound to play when a Red→Green (changes finished) toast fires.
    /// Defaults to <c>Default</c> — softer, informational.
    /// </summary>
    public string RedToGreenSound { get; set; } = "Default";

    [JsonIgnore]
    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CopilotSessionMonitor",
        "settings.json");

    private static readonly JsonSerializerOptions s_opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new AppSettings();
            var json = File.ReadAllText(Path);
            return JsonSerializer.Deserialize<AppSettings>(json, s_opts) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, s_opts));
        }
        catch (Exception ex)
        {
            DebugLog.Error("Settings.Save failed", ex);
        }
    }
}
