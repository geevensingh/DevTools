using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace CopilotSessionMonitor.Services;

/// <summary>
/// Best-effort focusing for a Copilot CLI session's host window.
///
/// Two complications make this non-trivial:
///   1. Windows Terminal hosts many tabs in a single window. SetForegroundWindow
///      alone leaves whichever tab was last active selected. We use UIA to find
///      the tab whose Name matches the session's summary and Select it first.
///   2. Windows Terminal also (by default) runs a single process for multiple
///      windows. <see cref="Process.MainWindowHandle"/> picks one HWND
///      arbitrarily — likely the wrong one. We enumerate all top-level windows
///      owned by that PID and pick the one whose UIA tree contains a matching
///      tab.
/// </summary>
public static class TerminalFocuser
{
    public static bool TryFocus(int pid, string? sessionSummary, string? sessionCwd, IReadOnlyCollection<string>? knownTabTitles = null)
    {
        try
        {
            // Walk up to find any host process with a top-level window.
            var hostPid = FindHostPidWithWindow(pid, out var hostName);
            if (hostPid == 0) return false;

            IntPtr hwndToFocus;

            if (string.Equals(hostName, "WindowsTerminal", StringComparison.OrdinalIgnoreCase))
            {
                hwndToFocus = ResolveWindowsTerminalWindow(hostPid, sessionSummary, sessionCwd, knownTabTitles);
            }
            else
            {
                hwndToFocus = MainWindowHandleFor(hostPid);
            }

            if (hwndToFocus == IntPtr.Zero) return false;

            if (IsIconic(hwndToFocus)) ShowWindow(hwndToFocus, SW_RESTORE);
            return SetForegroundWindow(hwndToFocus);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enumerate all top-level windows owned by the WT PID, score each by
    /// whether its UIA tab strip contains a tab matching our session, select
    /// the matching tab in the winning window, and return its HWND.
    /// Falls back to the first WT window when no confident match is found.
    /// </summary>
    private static IntPtr ResolveWindowsTerminalWindow(int wtPid, string? summary, string? cwd, IReadOnlyCollection<string>? knownTabTitles)
    {
        var candidates = EnumerateTopLevelWindows(wtPid);
        if (candidates.Count == 0) return IntPtr.Zero;
        if (candidates.Count == 1)
        {
            // Single window — only the tab needs picking.
            TrySelectMatchingTab(candidates[0], summary, cwd, knownTabTitles);
            FocusTermControl(candidates[0]);
            return candidates[0];
        }

        IntPtr bestHwnd = IntPtr.Zero;
        int bestScore = 0;
        AutomationElement? bestTab = null;

        foreach (var hwnd in candidates)
        {
            var (tab, score) = ScoreBestTab(hwnd, summary, cwd, knownTabTitles);
            if (score > bestScore)
            {
                bestScore = score;
                bestTab = tab;
                bestHwnd = hwnd;
            }
        }

        if (bestTab is not null && bestScore >= 6)
        {
            TryInvokeSelect(bestTab);
            FocusTermControl(bestHwnd);
            return bestHwnd;
        }

        // No confident match across windows. Fall back to whichever window is
        // currently foreground-most among the candidates (least disruptive).
        return PickMostRecentlyActive(candidates);
    }

    /// <summary>
    /// After switching to the right tab, move keyboard focus into the
    /// TermControl pane itself so the user can immediately start typing.
    /// Without this, focus often stays on the tab strip and the user has
    /// to click the content area before keystrokes are received.
    /// </summary>
    private static void FocusTermControl(IntPtr wtHwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(wtHwnd);
            if (root is null) return;

            // TermControl is only present in the UIA tree for the *currently
            // active* tab — which is exactly the one we just selected — so a
            // descendant search finds the right pane.
            var term = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty, "TermControl"));

            if (term is not null)
            {
                term.SetFocus();
            }
        }
        catch
        {
            // SetFocus throws if the element disappeared mid-call (e.g. user
            // closed the tab between our Select and our Focus). Best-effort.
        }
    }

    private static void TrySelectMatchingTab(IntPtr wtHwnd, string? summary, string? cwd, IReadOnlyCollection<string>? knownTabTitles)
    {
        var (tab, score) = ScoreBestTab(wtHwnd, summary, cwd, knownTabTitles);
        if (tab is not null && score >= 6)
        {
            TryInvokeSelect(tab);
        }
    }

    private static (AutomationElement? tab, int score) ScoreBestTab(IntPtr wtHwnd, string? summary, string? cwd, IReadOnlyCollection<string>? knownTabTitles)
    {
        try
        {
            var root = AutomationElement.FromHandle(wtHwnd);
            if (root is null) return (null, 0);

            var tabCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
            var tabs = root.FindAll(TreeScope.Descendants, tabCond);
            if (tabs.Count == 0) return (null, 0);

            string? cwdLeaf = null;
            if (!string.IsNullOrWhiteSpace(cwd))
            {
                try { cwdLeaf = Path.GetFileName(cwd!.TrimEnd('\\', '/')); } catch { cwdLeaf = null; }
            }

            HashSet<string>? knownNormalized = null;
            if (knownTabTitles is { Count: > 0 })
            {
                knownNormalized = new HashSet<string>(knownTabTitles, StringComparer.OrdinalIgnoreCase);
            }

            AutomationElement? best = null;
            int bestScore = 0;
            foreach (AutomationElement tab in tabs)
            {
                var name = tab.Current.Name ?? string.Empty;
                int score = ScoreTab(name, summary, cwdLeaf, knownNormalized);
                if (score > bestScore) { bestScore = score; best = tab; }
            }
            return (best, bestScore);
        }
        catch
        {
            return (null, 0);
        }
    }

    private static int ScoreTab(string tabName, string? summary, string? cwdLeaf, HashSet<string>? knownNormalized)
    {
        if (string.IsNullOrEmpty(tabName)) return 0;
        var normalized = CopilotSessionMonitor.Core.SessionTailer.NormalizeTitle(tabName);
        int score = 0;

        // Strongest signal: tab title equals one we've previously observed for
        // this session (workspace summary OR a report_intent text). This survives
        // Copilot mutating the title as it works, since we accumulate every
        // intent the agent has reported into a per-session known-titles set.
        if (knownNormalized is not null && knownNormalized.Contains(normalized))
            score += 12;

        // Weaker fallbacks for sessions with no recorded intents (e.g. a brand-
        // new session whose only known title is the summary).
        if (!string.IsNullOrWhiteSpace(summary))
        {
            if (string.Equals(tabName, summary, StringComparison.OrdinalIgnoreCase)) score += 8;
            else if (tabName.Contains(summary!, StringComparison.OrdinalIgnoreCase)) score += 5;
        }

        if (!string.IsNullOrWhiteSpace(cwdLeaf) && tabName.Contains(cwdLeaf!, StringComparison.OrdinalIgnoreCase))
            score += 2;

        return score;
    }

    private static void TryInvokeSelect(AutomationElement tab)
    {
        try
        {
            if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pat))
            {
                ((SelectionItemPattern)pat).Select();
            }
        }
        catch { /* UIA can fail mid-call when the window is being torn down */ }
    }

    private static IntPtr PickMostRecentlyActive(List<IntPtr> candidates)
    {
        // GetWindow(GW_HWNDPREV) order roughly matches Z-order; the foreground
        // window comes first when iterated. We use GetForegroundWindow as a
        // tiebreaker, but if it's not in our candidate list we just return the
        // first candidate.
        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && candidates.Contains(fg)) return fg;
        return candidates[0];
    }

    private static List<IntPtr> EnumerateTopLevelWindows(int pid)
    {
        var list = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out int wndPid);
            if (wndPid == pid)
            {
                // Filter out tooltips / hidden owned popups: must have a non-empty title.
                int len = GetWindowTextLength(hwnd);
                if (len > 0) list.Add(hwnd);
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static int FindHostPidWithWindow(int pid, out string? processName)
    {
        processName = null;
        int current = pid;
        for (int i = 0; i < 6 && current > 0; i++)
        {
            var (hwnd, name) = MainWindowHandleAndNameFor(current);
            if (hwnd != IntPtr.Zero) { processName = name; return current; }

            var parent = GetParentPid(current);
            if (parent <= 0 || parent == current) break;
            current = parent;
        }
        return 0;
    }

    private static IntPtr MainWindowHandleFor(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.MainWindowHandle; }
        catch { return IntPtr.Zero; }
    }

    private static (IntPtr hwnd, string? processName) MainWindowHandleAndNameFor(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return (p.MainWindowHandle, p.ProcessName); }
        catch { return (IntPtr.Zero, null); }
    }

    private static int GetParentPid(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            var pbi = new PROCESS_BASIC_INFORMATION();
            int status = NtQueryInformationProcess(p.Handle, 0, ref pbi, Marshal.SizeOf(pbi), out _);
            if (status != 0) return 0;
            return (int)pbi.InheritedFromUniqueProcessId.ToInt64();
        }
        catch { return 0; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;
}



