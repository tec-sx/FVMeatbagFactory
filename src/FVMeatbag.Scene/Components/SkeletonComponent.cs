using FVMeatbag.Core.Import;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.Scene.Components;

/// <summary>
/// Shows the bones of a skinned mesh. It sits under the <see cref="MeshComponent"/> it belongs to,
/// so it follows the mesh through the transform graph.
/// </summary>
public sealed class SkeletonComponent : SceneComponent
{
    private bool _visible = true;

    public SkeletonComponent(Skeleton skeleton) : base("Skeleton")
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        Skeleton = skeleton;
    }

    public Skeleton Skeleton { get; }

    /// <summary>Builds the bone lines; exists while the component is attached.</summary>
    public Node<BoneLinesInput, BoneLinesOutput>? LinesNode { get; private set; }

    public bool Visible
    {
        get => _visible;
        set
        {
            _visible = value;
            LinesNode?.UpdateInputs((ref BoneLinesInput i) => i.Visible = value);
        }
    }

    protected override void OnAttached(Graph.Graph graph)
    {
        LinesNode = graph.AddNode(
            new BoneLinesOperation(),
            $"{Name}.Lines",
            new BoneLinesInput { Skeleton = Skeleton, Visible = _visible, World = WorldMatrix });
        graph.Connect(TransformNode!.Output(nameof(WorldTransformOutput.World)), LinesNode.Input(nameof(BoneLinesInput.World)));
    }

    protected override void OnDetaching() => LinesNode = null;
}
