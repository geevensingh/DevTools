using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Hand-rolled JSON for <c>recents.json</c>. Same approach as
/// <see cref="SettingsJsonSerializer"/> — discriminated <see cref="DiffSide"/>
/// gets a <c>"type": "commit" | "workingTree"</c> tag, output is camelCase
/// + indented for hand-editability.
///
/// <para>Round-trip contract: <c>Deserialize(Serialize(d)) == d</c> for any
/// well-formed <see cref="RecentsDoc"/>. Unknown / future versions
/// deserialize to <see cref="RecentsDoc.Empty"/> (not an exception) so a
/// downgrade can't corrupt the on-disk state — same downgrade-safety
/// strategy used by <see cref="SettingsService"/>.</para>
/// </summary>
internal static class RecentsJsonSerializer
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(RecentsDoc doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var items = new JsonArray();
        foreach (var item in doc.Items)
        {
            items.Add(SerializeItem(item));
        }

        var root = new JsonObject
        {
            ["version"] = doc.Version,
            ["items"] = items,
        };
        return root.ToJsonString(WriteOptions);
    }

    public static RecentsDoc Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return RecentsDoc.Empty;

        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException) { return RecentsDoc.Empty; }

        if (node is not JsonObject obj) return RecentsDoc.Empty;

        var version = TryInt(obj, "version") ?? 0;
        // Forward compatibility: an unknown future version is treated as
        // empty so a downgrade can't lose data silently — the writer will
        // re-stamp it at CurrentVersion next time the user records a launch.
        if (version != RecentsDoc.CurrentVersion) return RecentsDoc.Empty;

        if (obj["items"] is not JsonArray rawItems) return RecentsDoc.Empty;

        var items = new List<RecentLaunchContext>(rawItems.Count);
        foreach (var raw in rawItems)
        {
            if (raw is JsonObject o && TryDeserializeItem(o) is { } item)
            {
                items.Add(item);
            }
        }
        return new RecentsDoc(RecentsDoc.CurrentVersion, items);
    }

    private static JsonObject SerializeItem(RecentLaunchContext item) =>
        new()
        {
            ["repoPath"] = item.Identity.CanonicalRepoPath,
            ["left"] = SerializeSide(item.LeftDisplay),
            ["right"] = SerializeSide(item.RightDisplay),
            ["lastUsedUtc"] = item.LastUsedUtc.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            // Identity sides are derived from display sides today (Phase 3:
            // Identity.Left / Right reuse the user's input verbatim). We
            // serialize the display sides only and rebuild Identity on
            // load via ContextIdentityFactory.Create. Keeping it that way
            // means a future canonicalization change to identity (e.g.
            // resolving symbolic refs) doesn't invalidate the on-disk file.
        };

    private static JsonObject SerializeSide(DiffSide side) => side switch
    {
        DiffSide.WorkingTree => new JsonObject { ["type"] = "workingTree" },
        DiffSide.CommitIsh c => new JsonObject
        {
            ["type"] = "commit",
            ["reference"] = c.Reference,
        },
        _ => throw new InvalidOperationException($"Unknown DiffSide: {side.GetType().Name}"),
    };

    private static RecentLaunchContext? TryDeserializeItem(JsonObject obj)
    {
        var repoPath = TryString(obj, "repoPath");
        if (string.IsNullOrWhiteSpace(repoPath)) return null;

        var left = TryDeserializeSide(obj["left"]);
        var right = TryDeserializeSide(obj["right"]);
        if (left is null || right is null) return null;

        var lastUsedRaw = TryString(obj, "lastUsedUtc");
        if (string.IsNullOrEmpty(lastUsedRaw) ||
            !DateTimeOffset.TryParse(
                lastUsedRaw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var lastUsed))
        {
            return null;
        }

        var identity = ContextIdentityFactory.Create(repoPath, left, right);
        return new RecentLaunchContext(identity, left, right, lastUsed);
    }

    private static DiffSide? TryDeserializeSide(JsonNode? node)
    {
        if (node is not JsonObject obj) return null;
        var type = TryString(obj, "type");
        return type switch
        {
            "workingTree" => new DiffSide.WorkingTree(),
            "commit" when TryString(obj, "reference") is { } r && r.Length > 0 => new DiffSide.CommitIsh(r),
            _ => null,
        };
    }

    private static string? TryString(JsonObject obj, string key) =>
        obj[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? TryInt(JsonObject obj, string key) =>
        obj[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
}
