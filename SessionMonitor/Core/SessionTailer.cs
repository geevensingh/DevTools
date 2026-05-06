using System.IO;
using System.Text;
using System.Text.Json;

namespace CopilotSessionMonitor.Core;

/// <summary>
/// Tails a single session's <c>events.jsonl</c> file incrementally and folds
/// each event into the session's mutable <see cref="SessionState"/>. All file
/// access uses <c>FileShare.ReadWrite</c> so we coexist with the CLI process
/// that holds it open in append mode.
/// </summary>
public sealed class SessionTailer : IDisposable
{
    private readonly SessionState _state;
    private readonly string _eventsPath;
    private long _position;
    private readonly object _gate = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public SessionTailer(SessionState state)
    {
        _state = state;
        _eventsPath = Path.Combine(state.SessionDirectory, "events.jsonl");
    }

    /// <summary>Total events processed since the tailer was created. Used by the
    /// app's stats row.</summary>
    public static long TotalEventsProcessed => Interlocked.Read(ref s_totalEventsProcessed);

    private static long s_totalEventsProcessed;

    public string EventsPath => _eventsPath;

    /// <summary>
    /// Read all newly-appended bytes since the last call and fold each
    /// complete (newline-terminated) JSON line into the session state.
    /// Tolerates partial trailing lines and file truncation.
    /// </summary>
    public void Pump()
    {
        if (_disposed) return;
        lock (_gate)
        {
            if (!File.Exists(_eventsPath)) return;

            FileStream fs;
            try
            {
                fs = new FileStream(_eventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (IOException) { ErrorTally.Tally("tailer.openIO"); return; }
            catch (UnauthorizedAccessException) { ErrorTally.Tally("tailer.openAuth"); return; }

            using (fs)
            {
                if (fs.Length < _position)
                {
                    // File shrank (truncated/rotated) — replay from start.
                    _position = 0;
                    ResetFoldedState();
                }

                if (fs.Length == _position) return;

                fs.Position = _position;
                using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);

                string? line;
                long bytesProcessed = 0;
                while ((line = reader.ReadLine()) is not null)
                {
                    bytesProcessed = fs.Position;
                    if (line.Length == 0) continue;

                    try
                    {
                        ProcessLine(line);
                    }
                    catch (JsonException)
                    {
                        // Likely a partial line at the tail; will be re-read next pump.
                        ErrorTally.Tally("tailer.partialJson");
                        break;
                    }
                    catch (Exception)
                    {
                        ErrorTally.Tally("tailer.processLine");
                        break;
                    }
                }

                _position = AdvanceToLastNewline(fs, _position, bytesProcessed);
            }
        }
    }

    private static long AdvanceToLastNewline(FileStream fs, long oldPos, long candidatePos)
    {
        if (candidatePos <= oldPos) return oldPos;
        long target = Math.Min(candidatePos, fs.Length);
        if (target == 0) return 0;
        long pos = target;
        // Read backward looking for a '\n'.
        Span<byte> one = stackalloc byte[1];
        while (pos > oldPos)
        {
            fs.Position = pos - 1;
            int read = fs.Read(one);
            if (read == 1 && one[0] == (byte)'\n')
                return pos;
            pos--;
        }
        return oldPos;
    }

    private void ProcessLine(string json)
    {
        var env = JsonSerializer.Deserialize<EventEnvelope>(json, s_jsonOpts);
        if (env is null || string.IsNullOrEmpty(env.Type)) return;

        Interlocked.Increment(ref s_totalEventsProcessed);

        if (env.Timestamp is { } ts) _state.LastEventAt = ts;

        switch (env.Type)
        {
            case "session.start":
                if (env.Data is { } d1)
                {
                    var s = d1.Deserialize<SessionStartData>(s_jsonOpts);
                    if (s?.StartTime is { } st && _state.CreatedAt is null) _state.CreatedAt = st;
                }
                break;

            case "session.resume":
                if (env.Data is { } d2)
                {
                    var s = d2.Deserialize<SessionResumeData>(s_jsonOpts);
                    if (s?.Context is { } ctx)
                    {
                        if (!string.IsNullOrEmpty(ctx.BaseCommit)) _state.BaseCommit = ctx.BaseCommit;
                        if (!string.IsNullOrEmpty(ctx.Cwd)) _state.Cwd ??= ctx.Cwd;
                        if (!string.IsNullOrEmpty(ctx.Branch)) _state.Branch ??= ctx.Branch;
                        if (!string.IsNullOrEmpty(ctx.Repository)) _state.Repository ??= ctx.Repository;
                    }
                }
                break;

            case "assistant.turn_start":
                _state.InFlightTurn = true;
                break;
            case "assistant.turn_end":
                _state.InFlightTurn = false;
                break;

            case "tool.execution_start":
                if (env.Data is { } d3)
                {
                    var t = d3.Deserialize<ToolStartData>(s_jsonOpts);
                    if (t is not null && !string.IsNullOrEmpty(t.ToolCallId) && !string.IsNullOrEmpty(t.ToolName))
                    {
                        var description = ExtractDescription(t.Arguments);
                        _state.InFlightTools[t.ToolCallId] = new InFlightTool(
                            t.ToolCallId, t.ToolName, description, env.Timestamp ?? DateTimeOffset.UtcNow);
                        _state.LastToolName = t.ToolName;
                        _state.LastToolDescription = description;

                        AppendRecentEvent(env.Timestamp, RecentEventKind.Tool,
                            description is null ? t.ToolName : $"{t.ToolName}: {description}");

                        if (string.Equals(t.ToolName, "ask_user", StringComparison.OrdinalIgnoreCase))
                        {
                            _state.LastAskUserQuestion = ExtractStringField(t.Arguments, "question");
                        }
                        else if (string.Equals(t.ToolName, "report_intent", StringComparison.OrdinalIgnoreCase))
                        {
                            var intent = ExtractStringField(t.Arguments, "intent");
                            if (!string.IsNullOrWhiteSpace(intent))
                            {
                                AddKnownTitle(intent!);
                            }
                        }
                    }
                }
                break;

            case "assistant.message":
                if (env.Data is { } dm)
                {
                    var msg = dm.Deserialize<AssistantMessageData>(s_jsonOpts);
                    if (msg is not null)
                    {
                        if (msg.OutputTokens is { } ot && ot > 0) _state.OutputTokens += ot;
                        if (!string.IsNullOrWhiteSpace(msg.Content))
                        {
                            AppendRecentEvent(env.Timestamp, RecentEventKind.AssistantMessage,
                                Truncate(msg.Content!, 140));
                        }
                    }
                }
                break;

            case "user.message":
                if (env.Data is { } du)
                {
                    var um = du.Deserialize<UserMessageData>(s_jsonOpts);
                    if (um is not null && !string.IsNullOrWhiteSpace(um.Content))
                    {
                        AppendRecentEvent(env.Timestamp, RecentEventKind.UserMessage,
                            Truncate(um.Content!, 140));
                    }
                }
                break;

            case "tool.execution_complete":
                if (env.Data is { } d4)
                {
                    var t = d4.Deserialize<ToolCompleteData>(s_jsonOpts);
                    if (t is not null && !string.IsNullOrEmpty(t.ToolCallId))
                    {
                        if (_state.InFlightTools.TryGetValue(t.ToolCallId, out var tool) &&
                            string.Equals(tool.ToolName, "ask_user", StringComparison.OrdinalIgnoreCase))
                        {
                            _state.LastAskUserQuestion = null;
                        }
                        _state.InFlightTools.Remove(t.ToolCallId);
                    }
                }
                break;
        }
    }

    private static string? ExtractDescription(JsonElement? args)
    {
        if (args is not { } el) return null;
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (el.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String) return d.GetString();
        if (el.TryGetProperty("question", out var q) && q.ValueKind == JsonValueKind.String) return q.GetString();
        if (el.TryGetProperty("intent", out var i) && i.ValueKind == JsonValueKind.String) return i.GetString();
        return null;
    }

    private static string? ExtractStringField(JsonElement? args, string field)
    {
        if (args is not { } el) return null;
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (el.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
        return null;
    }

    private void ResetFoldedState()
    {
        _state.InFlightTurn = false;
        _state.InFlightTools.Clear();
        _state.LastToolName = null;
        _state.LastToolDescription = null;
        _state.LastAskUserQuestion = null;
        _state.OutputTokens = 0;
        _state.RecentEvents.Clear();
        // KnownTabTitles intentionally not cleared on truncation: WT may
        // still be displaying an old title, so old entries remain useful.
    }

    private void AppendRecentEvent(DateTimeOffset? at, RecentEventKind kind, string text)
    {
        var ev = new RecentEvent(at ?? DateTimeOffset.UtcNow, kind, text);
        var q = _state.RecentEvents;
        q.Enqueue(ev);
        while (q.Count > SessionState.RecentEventsCapacity) q.Dequeue();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max).TrimEnd() + "\u2026";

    private void AddKnownTitle(string raw)
    {
        var normalized = NormalizeTitle(raw);
        if (string.IsNullOrEmpty(normalized)) return;
        _state.KnownTabTitles.Add(normalized);

        // Bound the set so a long-running session doesn't grow unbounded.
        // 32 entries comfortably covers a session's history of intents
        // without pathological memory use.
        if (_state.KnownTabTitles.Count > 64)
        {
            // We don't track insertion order; simplest cheap eviction is to
            // keep the set small enough that this rarely matters. If it ever
            // becomes problematic, switch to an LRU.
            _state.KnownTabTitles.Clear();
            _state.KnownTabTitles.Add(normalized);
            if (!string.IsNullOrEmpty(_state.Summary))
                _state.KnownTabTitles.Add(NormalizeTitle(_state.Summary!));
        }
    }

    /// <summary>Lowercase, trim, strip a leading emoji + space (Copilot CLI prefixes intents with e.g. "🤖 ").</summary>
    internal static string NormalizeTitle(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        // Strip a single leading non-letter, non-digit prefix glyph + optional space.
        // Covers 🤖, 🐳, ⚡, ✨, etc. without enumerating every emoji.
        if (s.Length > 0 && !char.IsLetterOrDigit(s, 0))
        {
            int i = 0;
            // Skip surrogate pair if present.
            if (char.IsHighSurrogate(s[0]) && s.Length > 1 && char.IsLowSurrogate(s[1])) i = 2;
            else i = 1;
            // Skip a following space.
            if (i < s.Length && s[i] == ' ') i++;
            if (i < s.Length) s = s.Substring(i).TrimStart();
        }
        return s.ToLowerInvariant();
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
