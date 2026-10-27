using System.Collections.ObjectModel;
using FVMeatbag.Scene;
using FVMeatbag.Scene.Components;

namespace FVMeatbag.App.ViewModels;

/// <summary>Outliner entry for a scene component.</summary>
public sealed class SceneComponentViewModel(SceneComponent component) : ViewModelBase
{
    public SceneComponent Component { get; } = component;

    public string Name => Component.Name;

    public ObservableCollection<SceneComponentViewModel> Children { get; } =
        new(component.Children.Where(c => c is not GizmoComponent).Select(c => new SceneComponentViewModel(c)));
}
