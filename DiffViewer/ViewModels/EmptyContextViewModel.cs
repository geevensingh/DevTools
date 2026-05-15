using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DiffViewer.ViewModels;

/// <summary>
/// Cold-launch fallback shell: shown when <see cref="App.OnStartup"/>
/// fails to build a context from command-line args but at least one
/// recent is persisted. Exposes only <see cref="Recents"/>; all other
/// <see cref="MainViewModel"/> bindings (file list, diff pane, key
/// bindings, etc.) silently no-op against this VM because the bound
/// properties don't exist — that's the point of the marker
/// <see cref="IShellViewModel"/> interface.
/// </summary>
public sealed class EmptyContextViewModel : ObservableObject, IShellViewModel, IDisposable
{
    public RecentContextsViewModel Recents { get; }

    /// <summary>User-facing message rendered next to the recents dropdown.</summary>
    public string Message { get; }

    public EmptyContextViewModel(RecentContextsViewModel recents, string message)
    {
        Recents = recents ?? throw new ArgumentNullException(nameof(recents));
        Message = message ?? string.Empty;
    }

    public void Dispose() => Recents.Dispose();
}
