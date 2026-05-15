using System;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public class EmptyContextViewModelTests
{
    [Fact]
    public void Ctor_NullRecents_Throws()
    {
        Action act = () => new EmptyContextViewModel(null!, "msg");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_StoresRecentsAndMessage()
    {
        var svc = new FakeRecentsService();
        var recents = new RecentContextsViewModel(svc, switcher: null, currentIdentity: null);

        var vm = new EmptyContextViewModel(recents, "hello");
        vm.Recents.Should().BeSameAs(recents);
        vm.Message.Should().Be("hello");

        vm.Dispose();
    }

    [Fact]
    public void Dispose_DisposesRecents()
    {
        var svc = new FakeRecentsService();
        var recents = new RecentContextsViewModel(svc, switcher: null, currentIdentity: null);
        var vm = new EmptyContextViewModel(recents, "");

        vm.Dispose();

        // After Dispose, the VM no longer responds to service.Changed.
        var changes = 0;
        recents.PropertyChanged += (_, _) => changes++;
        svc.RaiseChanged();
        changes.Should().Be(0);
    }

    private sealed class FakeRecentsService : DiffViewer.Services.IRecentContextsService
    {
        public System.Collections.Generic.IReadOnlyList<DiffViewer.Models.RecentLaunchContext> Current { get; } = Array.Empty<DiffViewer.Models.RecentLaunchContext>();
        public event EventHandler? Changed;
        public System.Threading.Tasks.Task RecordLaunchAsync(DiffViewer.Models.ContextIdentity identity, DiffViewer.Models.DiffSide leftDisplay, DiffViewer.Models.DiffSide rightDisplay, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task RemoveAsync(DiffViewer.Models.ContextIdentity identity, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
