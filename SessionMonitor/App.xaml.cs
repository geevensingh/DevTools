using System.Configuration;
using System.Data;
using System.Runtime.InteropServices;
using System.Windows;

namespace CopilotSessionMonitor;

/// <summary>
/// WPF entry point. Single-instance enforcement and tray bootstrap happen here
/// (rather than spawning a main window) — the app lives in the system tray.
/// </summary>
public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "CopilotSessionMonitor.SingleInstance.{a4f5b1c8-4e6a-4d10-9d56-1f3f2c0b5d11}";
    private System.Threading.Mutex? _singleInstanceMutex;
    private TrayHost? _trayHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            AttachConsole(-1);
            using var fileWriter = new System.IO.StreamWriter(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SessionMonitor.selftest.log"));
            var multi = new MultiTextWriter(Console.Out, fileWriter);
            Console.SetOut(multi);
            int code = SelfTests.Run();
            Console.Out.Flush();
            fileWriter.Flush();
            Shutdown(code);
            return;
        }

        DebugLog.Info($"Startup pid={Environment.ProcessId} args=[{string.Join(' ', e.Args)}]");

        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
            DebugLog.Error("AppDomain.UnhandledException", ev.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, ev) =>
        {
            DebugLog.Error("Dispatcher.UnhandledException", ev.Exception);
            ev.Handled = true;
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            DebugLog.Error("TaskScheduler.UnobservedTaskException", ev.Exception);
            ev.SetObserved();
        };

        bool createdNew;
        _singleInstanceMutex = new System.Threading.Mutex(initiallyOwned: true, SingleInstanceMutexName, out createdNew);
        if (!createdNew)
        {
            DebugLog.Info("Another instance already running; exiting.");
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            _trayHost = new TrayHost();
            _trayHost.Start();
            DebugLog.Info("TrayHost started.");
        }
        catch (Exception ex)
        {
            DebugLog.Error("TrayHost.Start failed", ex);
            // Surface the failure rather than dying silently in the background.
            System.Windows.MessageBox.Show($"Copilot Session Monitor failed to start.\n\n{ex.GetType().Name}: {ex.Message}\n\nLog: {DebugLog.Path}",
                "Copilot Session Monitor", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayHost?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);
}

internal sealed class MultiTextWriter : System.IO.TextWriter
{
    private readonly System.IO.TextWriter[] _writers;
    public MultiTextWriter(params System.IO.TextWriter[] writers) => _writers = writers;
    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
    public override void Write(char value) { foreach (var w in _writers) w.Write(value); }
    public override void Write(string? value) { foreach (var w in _writers) w.Write(value); }
    public override void WriteLine(string? value) { foreach (var w in _writers) w.WriteLine(value); }
    public override void WriteLine() { foreach (var w in _writers) w.WriteLine(); }
    public override void Flush() { foreach (var w in _writers) w.Flush(); }
}
