using System.ComponentModel;

namespace DiffViewer;

/// <summary>
/// Marker interface for any view-model that can be set as the
/// <see cref="System.Windows.Window.DataContext"/> of <see cref="MainWindow"/>.
/// Currently implemented by <see cref="ViewModels.MainViewModel"/> (the
/// loaded-context shell) and <see cref="ViewModels.EmptyContextViewModel"/>
/// (the cold-launch fallback shell that exposes only the recents
/// dropdown). MainWindow.xaml uses implicit DataTemplates keyed on the
/// concrete type to render the appropriate body.
/// </summary>
public interface IShellViewModel : INotifyPropertyChanged
{
}
