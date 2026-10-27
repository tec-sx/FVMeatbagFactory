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
/// Move/rotate/scale interaction nodes are added to its graph in a later milestone.
/// </summary>
public sealed class GizmoComponent(SceneComponent target) : SceneComponent($"Gizmo({target.Name})")
{
    public SceneComponent Target { get; } = target;
    public GizmoMode Mode { get; set; } = GizmoMode.Translate;

    protected override OutputPort? ResolveParentWorld() =>
        Target.TransformNode?.Output(nameof(WorldTransformOutput.World));
}
