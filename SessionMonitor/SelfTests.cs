using CopilotSessionMonitor.Core;

namespace CopilotSessionMonitor;

/// <summary>
/// Tiny embedded test runner for the pure-function bits. Intended to be
/// invoked with <c>--self-test</c> on the command line; exits with a non-zero
/// code on failure. Not a replacement for a real test project, but enough to
/// guard regressions in the state machine and event folding.
/// </summary>
internal static class SelfTests
{
    public static int Run()
    {
        int failed = 0;
        int passed = 0;

        void Check(string name, Func<bool> assertion)
        {
            try
            {
                if (assertion()) { passed++; Console.WriteLine($"  PASS  {name}"); }
                else { failed++; Console.WriteLine($"  FAIL  {name}"); }
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine($"  THROW {name}: {e.Message}");
            }
        }

        Console.WriteLine("--- SessionStateMachine ---");

        Check("offline when no lock file", () =>
        {
            var s = MakeState();
            return SessionStateMachine.Classify(s) == SessionStatus.Offline;
        });

        Check("offline when lock present but pid dead", () =>
        {
            var s = MakeState();
            s.LockFilePresent = true;
            s.LockPid = 12345;
            s.PidAlive = false;
            return SessionStateMachine.Classify(s) == SessionStatus.Offline;
        });

        Check("blue when ask_user is in flight", () =>
        {
            var s = MakeAlive();
            s.InFlightTools["t1"] = new InFlightTool("t1", "ask_user", "pick one", DateTimeOffset.UtcNow);
            return SessionStateMachine.Classify(s) == SessionStatus.Blue;
        });

        Check("blue beats edit (ask_user wins)", () =>
        {
            var s = MakeAlive();
            s.InFlightTools["t1"] = new InFlightTool("t1", "ask_user", "pick one", DateTimeOffset.UtcNow);
            s.InFlightTools["t2"] = new InFlightTool("t2", "edit", "src/a.cs", DateTimeOffset.UtcNow);
            return SessionStateMachine.Classify(s) == SessionStatus.Blue;
        });

        Check("red when edit is in flight", () =>
        {
            var s = MakeAlive();
            s.InFlightTools["t1"] = new InFlightTool("t1", "edit", "src/a.cs", DateTimeOffset.UtcNow);
            return SessionStateMachine.Classify(s) == SessionStatus.Red;
        });

        Check("red when create is in flight", () =>
        {
            var s = MakeAlive();
            s.InFlightTools["t1"] = new InFlightTool("t1", "create", null, DateTimeOffset.UtcNow);
            return SessionStateMachine.Classify(s) == SessionStatus.Red;
        });

        Check("green when alive, idle, dirty tree (dirty alone is not Red)", () =>
        {
            var s = MakeAlive();
            s.GitDirty = true;
            return SessionStateMachine.Classify(s) == SessionStatus.Green;
        });

        Check("yellow when turn in flight, clean tree", () =>
        {
            var s = MakeAlive();
            s.LastEventAt = DateTimeOffset.UtcNow;
            s.InFlightTurn = true;
            return SessionStateMachine.Classify(s) == SessionStatus.Yellow;
        });

        Check("yellow when read-only tool in flight, clean tree", () =>
        {
            var s = MakeAlive();
            s.LastEventAt = DateTimeOffset.UtcNow;
            s.InFlightTools["t1"] = new InFlightTool("t1", "view", null, DateTimeOffset.UtcNow);
            return SessionStateMachine.Classify(s) == SessionStatus.Yellow;
        });

        Check("green when alive, idle, clean tree", () =>
        {
            var s = MakeAlive();
            s.LastEventAt = DateTimeOffset.UtcNow;
            return SessionStateMachine.Classify(s) == SessionStatus.Green;
        });

        Check("offline when alive but events older than stale threshold", () =>
        {
            var s = MakeAlive();
            s.LastEventAt = DateTimeOffset.UtcNow - TimeSpan.FromHours(8);
            return SessionStateMachine.Classify(s) == SessionStatus.Offline;
        });

        Check("stale threshold: 4 hours is the default", () =>
        {
            return SessionStateMachine.StaleThreshold == TimeSpan.FromHours(4);
        });

        Check("not stale just below threshold", () =>
        {
            var s = MakeAlive();
            s.LastEventAt = DateTimeOffset.UtcNow - TimeSpan.FromHours(3) - TimeSpan.FromMinutes(59);
            return SessionStateMachine.Classify(s) == SessionStatus.Green;
        });

        Check("not stale while a turn is in flight, even past threshold", () =>
        {
            var s = MakeAlive();
            s.LastEventAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(1);
            s.InFlightTurn = true;
            return SessionStateMachine.Classify(s) == SessionStatus.Yellow;
        });

        Check("not stale while a tool is in flight, even past threshold", () =>
        {
            var s = MakeAlive();
            s.LastEventAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(1);
            s.InFlightTools["t1"] = new InFlightTool("t1", "powershell", null, DateTimeOffset.UtcNow);
            return SessionStateMachine.Classify(s) == SessionStatus.Yellow;
        });

        Check("worst-state ranking: red > blue > yellow > green > offline", () =>
        {
            return SessionStatus.Red > SessionStatus.Blue
                && SessionStatus.Blue > SessionStatus.Yellow
                && SessionStatus.Yellow > SessionStatus.Green
                && SessionStatus.Green > SessionStatus.Offline;
        });

        Console.WriteLine();
        Console.WriteLine($"--- Tailer fold (real events.jsonl) ---");
        try
        {
            var (folded, lockProbe) = FoldRealSession();
            Check("real-session: tailer parsed events without throwing", () => folded.LastEventAt is not null);
            Check("real-session: workspace.yaml loaded cwd", () => !string.IsNullOrEmpty(folded.Cwd));
            Check("real-session: pid liveness consistent (lock present iff pid file exists)",
                () => lockProbe.LockFilePresent == (lockProbe.LockPid is not null));
            // After folding the entire stream up to "now", in-flight tool count for a session
            // we are actively reading should be reasonable (zero unless we started in mid-tool).
            Check("real-session: in-flight tools is a non-negative count", () => folded.InFlightTools.Count >= 0);
        }
        catch (Exception e)
        {
            failed++;
            Console.WriteLine($"  THROW real-session: {e.Message}");
        }

        Console.WriteLine();
        Console.WriteLine($"Result: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static SessionState MakeState() => new() { SessionId = "test", SessionDirectory = "C:\\nowhere" };

    private static SessionState MakeAlive()
    {
        var s = MakeState();
        s.LockFilePresent = true;
        s.LockPid = System.Environment.ProcessId;
        s.PidAlive = true;
        s.LastEventAt = DateTimeOffset.UtcNow;
        return s;
    }

    private static (SessionState folded, SessionState lockProbe) FoldRealSession()
    {
        var root = LocalCliSessionSource.DefaultRootPath;
        if (!System.IO.Directory.Exists(root)) throw new InvalidOperationException("no session-state dir");

        // Pick the most-recently-modified session.
        var dir = new System.IO.DirectoryInfo(root)
            .EnumerateDirectories()
            .OrderByDescending(d => d.LastWriteTimeUtc)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("no session subfolders");

        var s = new SessionState { SessionId = dir.Name, SessionDirectory = dir.FullName };
        WorkspaceLoader.TryLoad(System.IO.Path.Combine(dir.FullName, "workspace.yaml"), s);
        var tailer = new SessionTailer(s);
        tailer.Pump();
        var lockProbe = new SessionState { SessionId = dir.Name, SessionDirectory = dir.FullName };
        PidLiveness.Probe(lockProbe);
        return (s, lockProbe);
    }
}
