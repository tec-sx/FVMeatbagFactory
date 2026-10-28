using FVMeatbag.Core.Muscles;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.Scene.Components;

/// <summary>
/// Shows the muscles of a skeletal mesh as a see-through preview. It sits under the
/// <see cref="SkeletonComponent"/> whose pose moves them.
/// </summary>
/// <remarks>
/// The muscles belong to the mesh asset in the project hierarchy; this component mirrors their
/// version into the graph. Its graph is muscles + skeleton → rig (bones looked up), then rig + the
/// skeleton's pose → frames (placed, stretched, bulged) → shapes (preview meshes). Later stages
/// (skin push, corrective fit) read the frames.
/// </remarks>
public sealed class MuscleComponent(MuscleSet muscles) : SceneComponent("Muscles")
{
    private bool _visible = true;
    private Maybe<int> _selected;

    public MuscleSet Muscles { get; } = muscles ?? throw new ArgumentNullException(nameof(muscles));

    /// <summary>The skeleton component this one hangs under, while attached.</summary>
    public SkeletonComponent? Skeleton => Parent as SkeletonComponent;

    /// <summary>Looks up the muscles' bones; exists while attached under a skeleton.</summary>
    public Node<MuscleRigInput, MuscleRigOutput>? RigNode { get; private set; }

    /// <summary>Places the muscles in the current pose; exists while attached under a skeleton.</summary>
    public Node<MuscleFrameInput, MuscleFrameOutput>? FrameNode { get; private set; }

    /// <summary>Builds the preview meshes; exists while attached under a skeleton.</summary>
    public Node<MuscleShapeInput, MuscleShapeOutput>? ShapeNode { get; private set; }

    /// <summary>Raised when <see cref="SelectedMuscle"/> changes.</summary>
    public event EventHandler? SelectedMuscleChanged;

    public bool Visible
    {
        get => _visible;
        set
        {
            _visible = value;
            ShapeNode?.UpdateInputs((ref MuscleShapeInput i) => i.Visible = value);
        }
    }

    /// <summary>Index in <see cref="Muscles"/> of the highlighted muscle, or None.</summary>
    public Maybe<int> SelectedMuscle
    {
        get => _selected;
        set
        {
            var index = value.Where(i => i >= 0 && i < Muscles.Count);
            if (index == _selected)
            {
                return;
            }
            _selected = index;
            ShapeNode?.UpdateInputs((ref MuscleShapeInput i) => i.Selected = index);
            SelectedMuscleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnAttached(Graph.Graph graph)
    {
        if (Skeleton is not { PoseNode: { } poseNode } skeleton)
        {
            return;
        }
        RigNode = graph.AddNode(
            new MuscleRigOperation(),
            $"{Name}.Rig",
            new MuscleRigInput
            {
                Muscles = Muscles,
                MusclesVersion = Muscles.Version,
                Skeleton = skeleton.Skeleton,
                SkeletonVersion = skeleton.Skeleton.Version,
            });
        FrameNode = graph.AddNode(new MuscleFrameOperation(), $"{Name}.Frames");
        ShapeNode = graph.AddNode(
            new MuscleShapeOperation(),
            $"{Name}.Shapes",
            new MuscleShapeInput { Owner = Id, Visible = _visible, Selected = _selected });
        graph.Connect(RigNode.Output(nameof(MuscleRigOutput.Muscles)), FrameNode.Input(nameof(MuscleFrameInput.Muscles)));
        graph.Connect(poseNode.Output(nameof(PoseOutput.BoneWorld)), FrameNode.Input(nameof(MuscleFrameInput.BoneWorld)));
        graph.Connect(RigNode.Output(nameof(MuscleRigOutput.Muscles)), ShapeNode.Input(nameof(MuscleShapeInput.Muscles)));
        graph.Connect(FrameNode.Output(nameof(MuscleFrameOutput.Frames)), ShapeNode.Input(nameof(MuscleShapeInput.Frames)));
        Muscles.Changed += OnMusclesChanged;
        skeleton.Skeleton.Renamed += OnSkeletonRenamed;
    }

    protected override void OnDetaching()
    {
        Muscles.Changed -= OnMusclesChanged;
        if (Skeleton is { } skeleton)
        {
            skeleton.Skeleton.Renamed -= OnSkeletonRenamed;
        }
        RigNode = null;
        FrameNode = null;
        ShapeNode = null;
    }

    private void OnMusclesChanged(object? sender, EventArgs e)
    {
        RigNode?.UpdateInputs((ref MuscleRigInput i) => i.MusclesVersion = Muscles.Version);
        if (_selected.TryGetValue(out var selected) && selected >= Muscles.Count)
        {
            SelectedMuscle = Maybe<int>.None;
        }
    }

    private void OnSkeletonRenamed(object? sender, EventArgs e)
    {
        if (Skeleton is { } skeleton)
        {
            RigNode?.UpdateInputs((ref MuscleRigInput i) => i.SkeletonVersion = skeleton.Skeleton.Version);
        }
    }
}
