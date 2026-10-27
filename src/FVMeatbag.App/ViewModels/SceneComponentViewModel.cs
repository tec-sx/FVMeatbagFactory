using System.Collections.ObjectModel;
using FVMeatbag.Scene;

namespace FVMeatbag.App.ViewModels;

/// <summary>Outliner entry for a scene component.</summary>
public sealed class SceneComponentViewModel(SceneComponent component) : ViewModelBase
{
    public SceneComponent Component { get; } = component;
    public string Name => Component.Name;
    public ObservableCollection<SceneComponentViewModel> Children { get; } =
        new(component.Children.Select(c => new SceneComponentViewModel(c)));
}
