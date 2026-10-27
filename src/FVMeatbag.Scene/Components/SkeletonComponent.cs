using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.Scene.Components;

/// <summary>
/// Shows and poses the bones of a skinned mesh, Maya style, and holds the bone selection. It sits
/// under the <see cref="MeshComponent"/> it belongs to, so it follows the mesh through the
/// transform graph.
/// </summary>
/// <remarks>
/// The pose belongs to the mesh asset in the project hierarchy; this component edits it and
/// mirrors its version into the graph. Its graph is pose → bone display, and pose → skinning,
/// which gives the posed mesh for the renderer.
/// </remarks>
public sealed class SkeletonComponent : SceneComponent
{
    private readonly Maybe<MeshGeometry> _geometry;
    private bool _visible = true;
    private float _boneSize = 1f;
    private BoneColorMode _colorMode = BoneColorMode.BySide;
    private Maybe<int> _selectedBone;

    public SkeletonComponent(Skeleton skeleton) : this(new SkeletonPose(skeleton), Maybe<MeshGeometry>.None)
    {
    }

    public SkeletonComponent(SkeletonPose pose, Maybe<MeshGeometry> geometry) : base("Skeleton")
    {
        ArgumentNullException.ThrowIfNull(pose);
        Pose = pose;
        _geometry = geometry;
    }

    public Skeleton Skeleton => Pose.Skeleton;

    /// <summary>The pose being shown and edited, owned by the mesh asset.</summary>
    public SkeletonPose Pose { get; }

    /// <summary>Computes the posed bone transforms and skinning matrices; exists while attached.</summary>
    public Node<PoseInput, PoseOutput>? PoseNode { get; private set; }

    /// <summary>Builds the joint spheres and bone lines; exists while the component is attached.</summary>
    public Node<BoneDisplayInput, BoneDisplayOutput>? DisplayNode { get; private set; }

    /// <summary>Deforms the mesh by the pose; exists while attached and the mesh has geometry.</summary>
    public Node<SkinningInput, SkinningOutput>? SkinningNode { get; private set; }

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

    /// <summary>
    /// The bone's current world transform, computed from the pose right away rather than from the
    /// graph, so tools see their own edits before the next evaluation.
    /// </summary>
    public Matrix4x4 BoneWorldMatrix(int bone) => Pose.ComputeWorld()[bone] * CurrentWorldMatrix();

    /// <summary>World transform of the bone's parent; this component's world matrix for a root bone.</summary>
    public Matrix4x4 ParentWorldMatrix(int bone)
    {
        var parent = Skeleton.Bones[bone].Parent;
        return parent < 0 ? CurrentWorldMatrix() : BoneWorldMatrix(parent);
    }

    /// <summary>
    /// Sets the bone's translation so it moves by <paramref name="worldOffset"/> from where
    /// <paramref name="start"/> put it.
    /// </summary>
    public void MoveBone(int bone, BoneTransform start, Vector3 worldOffset)
    {
        var offset = ToParentSpace(bone, worldOffset);
        Pose.Set(bone, start with { Translation = start.Translation + offset });
    }

    /// <summary>
    /// Sets the bone's rotation to <paramref name="start"/> turned by <paramref name="radians"/>
    /// about <paramref name="worldAxis"/>, around the bone's own origin.
    /// </summary>
    public void RotateBone(int bone, BoneTransform start, Vector3 worldAxis, float radians)
    {
        var axis = ToParentSpace(bone, worldAxis);
        if (axis.LengthSquared() < 1e-12f)
        {
            return;
        }
        var turn = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), radians);
        // Row vectors: the bone's own rotation first, then the turn in its parent's space.
        Pose.Set(bone, start with { Rotation = Quaternion.Normalize(Quaternion.Concatenate(start.Rotation, turn)) });
    }

    /// <summary>Sets the bone's scale to <paramref name="start"/> times <paramref name="factors"/> along its own axes.</summary>
    public void ScaleBone(int bone, BoneTransform start, Vector3 factors) =>
        Pose.Set(bone, start with { Scale = start.Scale * Vector3.Max(factors, new Vector3(0.001f)) });

    protected override void OnAttached(Graph.Graph graph)
    {
        PoseNode = graph.AddNode(new PoseOperation(), $"{Name}.Pose", new PoseInput { Pose = Pose, Version = Pose.Version });
        Pose.Changed += OnPoseChanged;
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
        graph.Connect(PoseNode.Output(nameof(PoseOutput.BoneWorld)), DisplayNode.Input(nameof(BoneDisplayInput.BoneWorld)));
        if (_geometry.HasValue)
        {
            SkinningNode = graph.AddNode(new SkinningOperation(), $"{Name}.Skinning", new SkinningInput { Geometry = _geometry, IsRest = true });
            graph.Connect(PoseNode.Output(nameof(PoseOutput.Skin)), SkinningNode.Input(nameof(SkinningInput.Skin)));
            graph.Connect(PoseNode.Output(nameof(PoseOutput.IsRest)), SkinningNode.Input(nameof(SkinningInput.IsRest)));
        }
    }

    protected override void OnDetaching()
    {
        Pose.Changed -= OnPoseChanged;
        PoseNode = null;
        DisplayNode = null;
        SkinningNode = null;
    }

    private void OnPoseChanged(object? sender, EventArgs e) => PoseNode?.UpdateInputs((ref PoseInput i) => i.Version = Pose.Version);

    /// <summary>World matrix from the parent's current local transforms, so it is right even before the graph evaluates.</summary>
    private Matrix4x4 CurrentWorldMatrix()
    {
        var world = LocalTransform.ToMatrix();
        for (var parent = Parent; parent is not null; parent = parent.Parent)
        {
            world *= parent.LocalTransform.ToMatrix();
        }
        return world;
    }

    /// <summary>A world-space direction expressed in the bone's parent space.</summary>
    private Vector3 ToParentSpace(int bone, Vector3 worldDirection) =>
        Matrix4x4.Invert(ParentWorldMatrix(bone), out var inverse) ? Vector3.TransformNormal(worldDirection, inverse) : worldDirection;
}
