using FVMeatbag.Core.Import;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.Scene.Components;

/// <summary>
/// Shows the bones of a skinned mesh, Maya style, and holds the bone selection. It sits under the
/// <see cref="MeshComponent"/> it belongs to, so it follows the mesh through the transform graph.
/// </summary>
public sealed class SkeletonComponent : SceneComponent
{
    private bool _visible = true;
    private float _boneSize = 1f;
    private BoneColorMode _colorMode = BoneColorMode.BySide;
    private Maybe<int> _selectedBone;

    public SkeletonComponent(Skeleton skeleton) : base("Skeleton")
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        Skeleton = skeleton;
    }

    public Skeleton Skeleton { get; }

    /// <summary>Builds the joint spheres and bone lines; exists while the component is attached.</summary>
    public Node<BoneDisplayInput, BoneDisplayOutput>? DisplayNode { get; private set; }

    /// <summary>Raised when <see cref="SelectedBone"/> changes.</summary>
    public event EventHandler? SelectedBoneChanged;

    public bool Visible
    {
        get => _visible;
        set
        {
            _visible = value;
            DisplayNode?.UpdateInputs((ref BoneDisplayInput i) => i.Visible = value);
        }
    }

    /// <summary>Joint sphere and bone size; 1 is the default, like Maya's joint display scale.</summary>
    public float BoneSize
    {
        get => _boneSize;
        set
        {
            _boneSize = Math.Clamp(value, 0.05f, 20f);
            DisplayNode?.UpdateInputs((ref BoneDisplayInput i) => i.BoneSize = _boneSize);
        }
    }

    public BoneColorMode ColorMode
    {
        get => _colorMode;
        set
        {
            _colorMode = value;
            DisplayNode?.UpdateInputs((ref BoneDisplayInput i) => i.ColorMode = value);
        }
    }

    /// <summary>The selected bone's index in <see cref="Skeleton"/>, or None.</summary>
    public Maybe<int> SelectedBone
    {
        get => _selectedBone;
        set
        {
            var bone = value.Where(b => b >= 0 && b < Skeleton.Count);
            if (bone == _selectedBone)
            {
                return;
            }
            _selectedBone = bone;
            DisplayNode?.UpdateInputs((ref BoneDisplayInput i) => i.SelectedBone = bone);
            SelectedBoneChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnAttached(Graph.Graph graph)
    {
        DisplayNode = graph.AddNode(
            new BoneDisplayOperation(),
            $"{Name}.Display",
            new BoneDisplayInput
            {
                Skeleton = Skeleton,
                Visible = _visible,
                BoneSize = _boneSize,
                ColorMode = _colorMode,
                SelectedBone = _selectedBone,
                World = WorldMatrix,
            });
        graph.Connect(TransformNode!.Output(nameof(WorldTransformOutput.World)), DisplayNode.Input(nameof(BoneDisplayInput.World)));
    }

    protected override void OnDetaching() => DisplayNode = null;
}
