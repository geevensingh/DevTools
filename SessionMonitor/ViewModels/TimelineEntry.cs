namespace CopilotSessionMonitor.ViewModels;

/// <summary>One entry in the per-row event timeline. Plain record so WPF
/// data-binding picks it up without further plumbing.</summary>
public sealed record TimelineEntry(string TimeText, string Glyph, string Text);
