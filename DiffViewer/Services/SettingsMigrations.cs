using System.Text.Json.Nodes;

namespace DiffViewer.Services;

/// <summary>
/// Versioned migrations for <c>settings.json</c>. Each migration takes a
/// <see cref="JsonObject"/> shaped for version N and returns one shaped
/// for N+1. <see cref="MigrateUpTo"/> chains them.
///
/// <para>v1 is the inaugural shape, so there are no migrations yet. The
/// first time we change the shape we'll add a <c>MigrateV1ToV2</c> here
/// + a fixture test asserting it round-trips.</para>
/// </summary>
internal static class SettingsMigrations
{
    /// <summary>
    /// Run every migration in order from <paramref name="fromVersion"/>
    /// up to <paramref name="toVersion"/>. The returned object always
    /// carries a <c>schemaVersion</c> equal to <paramref name="toVersion"/>.
    /// </summary>
    public static JsonObject MigrateUpTo(JsonObject obj, int fromVersion, int toVersion)
    {
        var current = obj;
        for (int v = fromVersion; v < toVersion; v++)
        {
            Func<JsonObject, JsonObject> step = v switch
            {
                0 => MigrateV0ToV1, // pre-versioned files - treat as v1's shape
                _ => throw new InvalidOperationException($"No migration registered from version {v} to {v + 1}."),
            };
            current = step(current);
            current["schemaVersion"] = v + 1;
        }
        return current;
    }

    /// <summary>
    /// Pre-versioned (v0) files have the same shape as v1; we just stamp
    /// the version and let the deserializer fill in defaults for any
    /// missing field.
    /// </summary>
    private static JsonObject MigrateV0ToV1(JsonObject obj) => obj;
}
