using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public sealed class ExternalAppLauncherTests
{
    [Fact]
    public void ResolveEditor_NoConfig_StillRunsProbeFamilyOrder()
    {
        // Probe returns nothing for any name. Resolution may still succeed
        // via the well-known install-path scan if VS Code is installed on
        // this machine (likely on a dev box). Either way: when probe and
        // file system both miss, family must be None and path must be null.
        var probe = new FakeProbe(); // empty
        var launcher = new ExternalAppLauncher(probe: probe);
        var resolved = launcher.ResolveEditor();
        if (resolved.ExecutablePath is null)
        {
            resolved.Family.Should().Be(EditorFamily.None);
        }
        else
        {
            resolved.Family.Should().Be(EditorFamily.VsCodeFamily);
            File.Exists(resolved.ExecutablePath).Should().BeTrue();
        }
    }

    [Fact]
    public void ResolveEditor_ConfiguredPathExists_WinsOverProbe()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var settings = new InMemorySettingsService(new AppSettings { ExternalEditorPath = tmp });
            // Probe would resolve "code" - shouldn't matter because the
            // configured path takes priority.
            var launcher = new ExternalAppLauncher(settings, new FakeProbe { { "code", @"C:\code.cmd" } });
            var resolved = launcher.ResolveEditor();
            resolved.ExecutablePath.Should().Be(tmp);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void ResolveEditor_ConfiguredPathMissing_FallsBackToProbe()
    {
        var settings = new InMemorySettingsService(new AppSettings
        {
            ExternalEditorPath = @"C:\does\not\exist\code.cmd",
        });
        var launcher = new ExternalAppLauncher(settings, new FakeProbe { { "code", @"C:\code.cmd" } });
        var resolved = launcher.ResolveEditor();
        resolved.ExecutablePath.Should().Be(@"C:\code.cmd");
        resolved.Family.Should().Be(EditorFamily.VsCodeFamily);
    }

    [Fact]
    public void ResolveEditor_ProbesInPriorityOrder()
    {
        // Insiders is registered before stable. Stable should still win
        // because it's higher priority in the bin-name list.
        var probe = new FakeProbe
        {
            { "code", @"C:\code.cmd" },
            { "code-insiders", @"C:\code-insiders.cmd" },
        };
        var launcher = new ExternalAppLauncher(probe: probe);
        launcher.ResolveEditor().ExecutablePath.Should().Be(@"C:\code.cmd");
    }

    [Fact]
    public void ResolveEditor_FallsThroughToCursor_WhenOnlyCursorOnPath()
    {
        var probe = new FakeProbe { { "cursor", @"C:\cursor.cmd" } };
        var launcher = new ExternalAppLauncher(probe: probe);
        var resolved = launcher.ResolveEditor();
        resolved.ExecutablePath.Should().Be(@"C:\cursor.cmd");
        resolved.Family.Should().Be(EditorFamily.VsCodeFamily);
    }

    [Fact]
    public void ResolveEditor_CachesAfterFirstCall()
    {
        var probe = new FakeProbe { { "code", @"C:\code.cmd" } };
        var launcher = new ExternalAppLauncher(probe: probe);

        launcher.ResolveEditor();
        launcher.ResolveEditor();
        launcher.ResolveEditor();

        probe.CallCounts.GetValueOrDefault("code", 0).Should().Be(1, "subsequent ResolveEditor calls should hit the cache");
    }

    [Fact]
    public void ResolveEditor_ForceReDetect_BypassesCache()
    {
        var probe = new FakeProbe { { "code", @"C:\code.cmd" } };
        var launcher = new ExternalAppLauncher(probe: probe);

        launcher.ResolveEditor();
        launcher.ResolveEditor(forceReDetect: true);

        probe.CallCounts["code"].Should().Be(2);
    }

    // ---------------- Argument-list construction ----------------

    [Fact]
    public void BuildEditorProcessStart_VsCodeShim_UsesShellExecute()
    {
        var resolved = new EditorResolution(@"C:\code.cmd", EditorFamily.VsCodeFamily, "--goto {path}:{line}");
        var psi = ExternalAppLauncher.BuildEditorProcessStart(resolved, @"C:\repo\file.cs", 42);
        psi.UseShellExecute.Should().BeTrue(".cmd shim needs shell-execute so PATHEXT resolves it");
        psi.ArgumentList.Should().Equal(new[] { "--goto", @"C:\repo\file.cs:42" });
    }

    [Fact]
    public void BuildEditorProcessStart_RealExe_DoesNotUseShellExecute()
    {
        var resolved = new EditorResolution(@"C:\editor.exe", EditorFamily.Custom, "--goto {path}:{line}");
        var psi = ExternalAppLauncher.BuildEditorProcessStart(resolved, @"C:\repo\file.cs", 1);
        psi.UseShellExecute.Should().BeFalse(".exe doesn't need PATHEXT resolution");
    }

    [Theory]
    [InlineData(@"C:\repo\with space\file.cs", 10)]
    [InlineData(@"C:\repo\with&ampersand\file.cs", 1)]
    [InlineData(@"C:\repo\with""quote.cs", 5)]
    [InlineData(@"C:\repo\with%percent\file.cs", 99)]
    [InlineData(@"C:\repo\with^caret\file.cs", 50)]
    public void BuildEditorProcessStart_ShellHostilePaths_RoundTripUnmangled(string path, int line)
    {
        var resolved = new EditorResolution(@"C:\code.cmd", EditorFamily.VsCodeFamily, "--goto {path}:{line}");
        var psi = ExternalAppLauncher.BuildEditorProcessStart(resolved, path, line);
        // Each entry stays atomic - no shell parsing happens because we
        // use ArgumentList, not a single Arguments string.
        psi.ArgumentList.Should().Equal(new[] { "--goto", $"{path}:{line}" });
    }

    [Fact]
    public void BuildEditorProcessStart_NoLine_DropsGotoFlag()
    {
        var resolved = new EditorResolution(@"C:\code.cmd", EditorFamily.VsCodeFamily, "--goto {path}:{line}");
        var psi = ExternalAppLauncher.BuildEditorProcessStart(resolved, @"C:\repo\file.cs", 0);
        psi.ArgumentList.Should().Equal(new[] { @"C:\repo\file.cs" });
    }

    // ---------------- Helpers ----------------

    private sealed class FakeProbe : IEditorProbe, IEnumerable<KeyValuePair<string, string>>
    {
        private readonly Dictionary<string, string> _resolutions = new();
        public Dictionary<string, int> CallCounts { get; } = new();

        public void Add(string name, string path) => _resolutions[name] = path;
        public string? TryResolveOnPath(string binaryName)
        {
            CallCounts[binaryName] = CallCounts.GetValueOrDefault(binaryName, 0) + 1;
            return _resolutions.GetValueOrDefault(binaryName);
        }

        public System.Collections.IEnumerator GetEnumerator() => _resolutions.GetEnumerator();
        IEnumerator<KeyValuePair<string, string>> IEnumerable<KeyValuePair<string, string>>.GetEnumerator() =>
            _resolutions.GetEnumerator();
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        private AppSettings _current;
        public InMemorySettingsService(AppSettings initial) { _current = initial; }
        public AppSettings Current => _current;
        public SettingsLoadOutcome LastLoadOutcome => SettingsLoadOutcome.Loaded;
        public event System.EventHandler<SettingsChangedEventArgs>? Changed;
        public void Save(AppSettings updated)
        {
            var prev = _current;
            _current = updated;
            Changed?.Invoke(this, new SettingsChangedEventArgs(prev, _current));
        }
        public AppSettings Update(System.Func<AppSettings, AppSettings> mutate)
        {
            Save(mutate(_current));
            return _current;
        }
    }
}
