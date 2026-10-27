using FVMeatbag.Graph.Ports;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.Scene.Components;

public enum GizmoMode
{
    Translate,
    Rotate,
    Scale,
}

/// <summary>
/// Transform gizmo for the selected component. It sits under the scene root but its world matrix is
/// wired to the target's, so it follows the target through the graph rather than through code.
/// Moving whole components is a later milestone; bones are posed with <see cref="Tools.BoneManipulator"/>.
/// </summary>
public sealed class GizmoComponent(SceneComponent target) : SceneComponent($"Gizmo({target.Name})")
{
    public SceneComponent Target { get; } = target;
    public GizmoMode Mode { get; set; } = GizmoMode.Translate;

    protected override OutputPort? ResolveParentWorld() =>
        Target.TransformNode?.Output(nameof(WorldTransformOutput.World));
}
