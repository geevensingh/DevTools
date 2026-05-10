using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CopilotSessionMonitor.Core;

/// <summary>
/// Reads and parses <c>workspace.yaml</c> for a single session folder.
/// Cheap: file is &lt;1 KB. Caller decides when to re-read (on file mtime change).
/// </summary>
public static class WorkspaceLoader
{
    private static readonly IDeserializer s_deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static bool TryLoad(string workspaceYamlPath, SessionState target)
    {
        if (!File.Exists(workspaceYamlPath)) return false;
        try
        {
            using var reader = new StreamReader(workspaceYamlPath);
            var dto = s_deserializer.Deserialize<WorkspaceDto?>(reader);
            if (dto is null) return false;
            target.Cwd = dto.Cwd;
            target.GitRoot = dto.GitRoot;
            target.Repository = dto.Repository;
            target.Branch = dto.Branch;
            target.Name = dto.Name;
            target.Summary = dto.Summary;
            target.CreatedAt = dto.CreatedAt;
            target.UpdatedAt = dto.UpdatedAt;
            target.CloudSessionId = dto.McSessionId;
            target.CloudTaskId = dto.McTaskId;

            if (!string.IsNullOrWhiteSpace(dto.Summary))
                target.KnownTabTitles.Add(SessionTailer.NormalizeTitle(dto.Summary!));
            if (!string.IsNullOrWhiteSpace(dto.Name))
                target.KnownTabTitles.Add(SessionTailer.NormalizeTitle(dto.Name!));

            return true;
        }
        catch
        {
            ErrorTally.Tally("workspaceLoader.parse");
            return false;
        }
    }

    private sealed class WorkspaceDto
    {
        public string? Id { get; set; }
        public string? Cwd { get; set; }
        public string? GitRoot { get; set; }
        public string? Repository { get; set; }
        public string? Branch { get; set; }
        public string? Name { get; set; }
        public string? Summary { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public string? McSessionId { get; set; }
        public string? McTaskId { get; set; }
    }
}
