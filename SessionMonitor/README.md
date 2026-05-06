# Copilot Session Monitor

A Windows tray app that monitors all running [GitHub Copilot CLI](https://docs.github.com/en/copilot/github-copilot-cli) sessions on this machine and surfaces what each one is doing at a glance. Built as a way to manage the chaos of having a dozen Copilot CLI sessions open at once across multiple repos.

![Tray icon — worst-state aggregation across all live sessions]

## What it does

Per-session colour:

| Colour | Meaning |
| --- | --- |
| 🔴 Red    | An `edit` or `create` tool is in flight right now |
| 🔵 Blue   | An `ask_user` is open and the agent is blocked on you |
| 🟡 Yellow | The agent is working (in-flight turn or read-only tool) |
| 🟢 Green  | Process alive and idle, waiting on your next message |
| ⚫ Gray   | Process exited, or alive-but-inactive past the stale threshold |

The tray icon shows the **worst** state across all live sessions. Left-click the tray icon to expand a pinnable list-style window with one row per session, plus per-row actions (focus that session's terminal tab, open the repo or session-state folder in VS Code, kill the PID, mute notifications) and a word-wheel filter.

Toasts fire when a session enters Blue (needs you) or transitions Red→Green (finished a change). Click a session toast → the matching terminal tab is brought to the foreground; click the welcome toast → the main window opens. Toast aggregation, Focus Assist / DND suppression, and per-state sound choices are all built in.

## How it works

All data comes from the local files Copilot CLI already writes:

- `~/.copilot/session-state/<id>/inuse.<PID>.lock` — process liveness signal (lock file present + PID alive).
- `~/.copilot/session-state/<id>/events.jsonl` — append-only event log; tailed incrementally with both a 5 s heartbeat **and** a `FileSystemWatcher` on the file itself, so short-running tools (e.g. an `edit` that completes in < 100 ms) are still observable as in-flight.
- `~/.copilot/session-state/<id>/workspace.yaml` — repo, branch, cwd, summary.
- `git status --porcelain` in each session's cwd — used as a "tree dirty" indicator (does not by itself drive the Red state; only in-flight edit/create tools do).

## Build and run

```powershell
# Debug build
dotnet build SessionMonitor/SessionMonitor.csproj

# Run
.\SessionMonitor\bin\Debug\net8.0-windows\CopilotSessionMonitor.exe

# Run the embedded unit tests (results to stdout when invoked from a console
# that the .exe can attach to, and always to %TEMP%\SessionMonitor.selftest.log)
.\SessionMonitor\bin\Debug\net8.0-windows\CopilotSessionMonitor.exe --self-test

# Self-contained single-file Release publish (≈ 155 MB .exe, no .NET runtime needed on target)
dotnet publish SessionMonitor/SessionMonitor.csproj `
  -p:PublishProfile=Properties/PublishProfiles/win-x64-self-contained.pubxml
# Output: SessionMonitor\bin\Release\net8.0-windows\win-x64\publish\CopilotSessionMonitor.exe
```

The app enforces a single instance via a named mutex; double-launching is a no-op.

## Per-user state

Stored under `%LOCALAPPDATA%\CopilotSessionMonitor\`:

- `settings.json` — window size/position, pin, show-offline, group-by-repo, refresh interval, stale threshold, notification rules, per-state sounds, muted session ids.
- `app.log` — append-only diagnostic log: startup, heartbeat ticks (sampled), unhandled exceptions, accumulated swallowed-error counts. Rotated when it exceeds 10 MB (current → `app.log.old`).

Neither file is in this repo and neither needs to be deleted by hand to recover from a bad state — the app rewrites both with sensible defaults if missing or corrupt.

## Project layout

```
SessionMonitor/
├── SessionMonitor.csproj           SDK-style net8.0-windows, UseWPF + UseWindowsForms
├── App.xaml + App.xaml.cs          single-instance mutex, unhandled-exception logging, --self-test
├── TrayHost.cs                     owns the TaskbarIcon, aggregator, notifier, settings
├── AppSettings.cs                  JSON-persisted user prefs
├── DebugLog.cs                     %LOCALAPPDATA%\...\app.log writer (rotated)
├── SelfTests.cs                    embedded unit tests, run via --self-test
├── Core/
│   ├── SessionStatus.cs            the 5-state enum
│   ├── SessionState.cs             in-memory per-session state (mutable)
│   ├── EventModels.cs              JSON record types for events.jsonl
│   ├── SessionTailer.cs            incremental events.jsonl tailer + folder
│   ├── WorkspaceLoader.cs          workspace.yaml reader (YamlDotNet)
│   ├── PidLiveness.cs              parses inuse.<pid>.lock, probes Process.GetProcessById
│   ├── GitStatusProbe.cs           cached `git status --porcelain` runner
│   ├── SessionStateMachine.cs      pure (folded state) → SessionStatus classifier
│   ├── ISessionSource.cs           pluggable source interface (future: remote agent)
│   ├── LocalCliSessionSource.cs    the only impl today
│   ├── SessionAggregator.cs        merges sources, tracks per-session transitions + EnteredStatusAt
│   └── ErrorTally.cs               counter for swallowed exceptions (flushed to log)
├── Services/
│   ├── Notifier.cs                 transition → toast, with coalescing/DND/sounds
│   ├── TerminalFocuser.cs          UIA-based WT tab matching by title-history
│   ├── VsCodeLauncher.cs           Process.Start("code", path)
│   ├── TrayIconFactory.cs          renders the 32×32 tray icon at runtime
│   ├── AutostartToggle.cs          Startup-folder shortcut via WScript.Shell COM
│   └── MuteService.cs              per-session notification mute
├── ViewModels/
│   ├── MainWindowViewModel.cs      list + filter + sort + grouping + stats
│   ├── SessionRowViewModel.cs      per-row state, actions, timeline projection
│   └── TimelineEntry.cs
├── Views/
│   ├── MainWindow.xaml(.cs)        title bar, toolbar, list, footer
│   ├── SettingsWindow.xaml(.cs)    matching custom chrome
│   └── Converters.cs               BoolToVisibility / NonEmptyToVisibility
└── Properties/
    └── PublishProfiles/
        └── win-x64-self-contained.pubxml
```

## Known limitations

These are intentional v1 trade-offs, all called out in code comments where they live:

- **Output tokens only.** `events.jsonl` carries `outputTokens` per `assistant.message` but no input tokens or model name. Full input + output + per-model cost lives in `~/.copilot/session-store.db` (a SQLite store this app does not currently read). The footer's "N output tokens" label is honest about this; cost calculation is out of scope until the SQLite reader exists.
- **WT tab matching is title-based.** Windows Terminal does not publicly expose a `tab → OpenConsole PID` mapping, so "Focus terminal" finds the right tab by matching the workspace summary or any `report_intent` text the agent has emitted in this session. Renaming a tab manually breaks the match; otherwise it's reliable.
- **`Focus terminal` for non-WT hosts** (e.g., the VS Code integrated terminal) falls back to "focus the host process's main window" with no per-tab/pane awareness.
- **Legacy balloon-tip toasts.** The Windows balloon API doesn't support action buttons, custom durations, or Action Center persistence. Migrating to the modern Windows toast XML path (`Microsoft.Toolkit.Uwp.Notifications`) requires registering an AppUserModelID and ideally MSIX packaging — deferred.
- **Dark theme only.** The whole UI is hardcoded to a Fluent dark palette in `App.xaml`. Light/system theme is a known v1.1 task.
- **Local CLI sessions only.** Remote / cloud Copilot Coding Agent sessions are not surfaced. The data layer (`ISessionSource`) is shaped to allow a second source to plug in later.

## Smoke-test checklist (manual)

For end-to-end verification before shipping:

1. Tray icon appears; right-click context menu lists state counts; "Settings…" opens the dialog with matching chrome.
2. Left-click tray → main window appears at the saved position; close (✕) hides it; pin (📌) keeps it always-on-top.
3. Filter (Ctrl+F) narrows the list; clear (✕ / Esc) restores it.
4. "Show offline" + non-empty filter that matches only an offline row → row surfaces (graceful fallback rule).
5. "Group by repo" toggle clusters sessions; expanders collapse/expand groups.
6. Per-row actions all work: Focus terminal (UIA tab match), Copy session id (clipboard), Open repo in VS Code, Open session state, Reveal folder (Explorer), Mute (🔔 ↔ 🔕), Kill PID (with confirmation).
7. Settings dialog: Heartbeat / Stale Threshold / Notification toggles / Sound dropdowns; preview ▶ buttons play; Save applies live.
8. Window size + position survive a relaunch.
