namespace CopilotSessionMonitor.Core;

/// <summary>
/// The five visible states. Order matters for "worst state" aggregation:
/// numerically higher = more attention required. Red and Blue are both
/// "needs the user" (Red = pay attention to changes; Blue = literally blocked
/// on a question), but in practice Red ranks above Blue because an actively
/// changing repo is the higher-stakes signal to the user.
/// </summary>
public enum SessionStatus
{
    Offline = 0,
    Green = 1,
    Yellow = 2,
    Blue = 3,
    Red = 4,
}

public static class SessionStatusExtensions
{
    public static string DisplayName(this SessionStatus s) => s switch
    {
        SessionStatus.Red => "Making changes",
        SessionStatus.Blue => "Waiting on you",
        SessionStatus.Yellow => "Planning",
        SessionStatus.Green => "Idle",
        SessionStatus.Offline => "Offline",
        _ => s.ToString(),
    };
}
