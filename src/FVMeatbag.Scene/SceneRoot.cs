using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Project;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Components;
using FVMeatbag.Scene.Tools;

namespace FVMeatbag.Scene;

/// <summary>
/// The root scene. Its graph is a child of the application's root graph; every component's graph
/// hangs below it in the same shape as the component tree.
/// </summary>
/// <remarks>
/// Every scene has exactly one <see cref="Camera"/> and one <see cref="Light"/>. They are created
/// here, can be adjusted, and can never be removed or duplicated.
/// </remarks>
public sealed class SceneRoot
{
    private bool _showBones = true;
    private bool _showMuscles = true;
    private float _muscleOpacity = 0.6f;

    public SceneRoot(Graph.Graph parentGraph, string name = "Scene")
    {
        Graph = parentGraph.CreateChild(name);
        Root = new SceneComponent("Root");
        Root.Attach(this);
        Camera = Root.AddChild(new OrbitCameraComponent());
        Light = Root.AddChild(new DirectionalLightComponent());
        Manipulator = new BoneManipulator(this);
    }

    public Graph.Graph Graph { get; }
    public SceneComponent Root { get; }
    public OrbitCameraComponent Camera { get; }
    public DirectionalLightComponent Light { get; }
    public SceneComponent? Selected { get; private set; }
    public GizmoComponent? Gizmo { get; private set; }

    /// <summary>The viewport tool (select, move, rotate, scale) and its gizmo for the selected bone.</summary>
    public BoneManipulator Manipulator { get; }

    /// <summary>The project whose assets the scene shows.</summary>
    public Maybe<CharacterProject> Project { get; private set; }

    public IEnumerable<MeshComponent> Meshes => Root.DescendantsAndSelf().OfType<MeshComponent>();

    public IEnumerable<SkeletonComponent> Skeletons => Root.DescendantsAndSelf().OfType<SkeletonComponent>();

    public IEnumerable<MuscleComponent> MuscleComponents => Root.DescendantsAndSelf().OfType<MuscleComponent>();

    /// <summary>Shows or hides the bones of every skeleton, including ones added later.</summary>
    public bool ShowBones
    {
        get => _showBones;
        set
        {
            _showBones = value;
            foreach (var skeleton in Skeletons)
            {
                skeleton.Visible = value;
            }
        }
    }

    /// <summary>Shows or hides the muscle preview of every mesh, including ones added later.</summary>
    public bool ShowMuscles
    {
        get => _showMuscles;
        set
        {
            _showMuscles = value;
            foreach (var muscles in MuscleComponents)
            {
                muscles.Visible = value;
            }
        }
    }

    /// <summary>Opacity of the muscle preview, 0.05 to 1.</summary>
    public float MuscleOpacity
    {
        get => _muscleOpacity;
        set => _muscleOpacity = Math.Clamp(value, 0.05f, 1f);
    }

    /// <summary>True draws the muscles over the skin (X-ray); false hides them where the skin is in front.</summary>
    public bool MusclesThroughSkin { get; set; } = true;

    /// <summary>Clears the bone selection of every skeleton.</summary>
    public void ClearBoneSelection()
    {
        foreach (var skeleton in Skeletons)
        {
            skeleton.SelectedBone = Maybe<int>.None;
        }
    }

    public event EventHandler? SelectionChanged;

    /// <summary>Raised when the scene switches to another project.</summary>
    public event EventHandler? ProjectChanged;

    /// <summary>
    /// Shows <paramref name="project"/>: removes everything but the camera and light, applies the
    /// project's camera and light settings, and adds a mesh component for each mesh asset.
    /// </summary>
    public void Show(CharacterProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        Select(null);
        using (Graph.Evaluator.BeginBatch())
        {
            foreach (var child in Root.Children.Where(c => c.IsRemovable).ToList())
            {
                Root.RemoveChild(child);
            }
            Project = Maybe.Some(project);
            Camera.Settings = project.Camera;
            Light.Settings = project.Light;
            foreach (var asset in project.MeshAssets)
            {
                AddMesh(asset);
            }
        }
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds a component that displays <paramref name="asset"/>, moved so its bounds are centered on
    /// the origin. Rigged meshes get a <see cref="SkeletonComponent"/> child that shows the bones,
    /// with a <see cref="MuscleComponent"/> below it for the muscle preview.
    /// </summary>
    public MeshComponent AddMesh(SkeletalMeshAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var center = asset.Geometry.Map(g => g.Bounds.Center).OrDefault(Vector3.Zero);
        var mesh = new MeshComponent(asset.Name, asset.Id)
        {
            LocalTransform = Transform.Identity with { Position = -center },
        };
        if (asset.Pose.TryGetValue(out var pose))
        {
            var skeleton = new SkeletonComponent(pose, asset.Geometry) { Visible = ShowBones };
            skeleton.AddChild(new MuscleComponent(asset.Muscles) { Visible = ShowMuscles });
            mesh.AddChild(skeleton);
        }
        return Root.AddChild(mesh);
    }

    public Maybe<MeshComponent> FindMesh(Guid assetId) => Meshes.FirstOrNone(m => m.AssetId == assetId);

    /// <summary>The mesh asset a component displays, looked up in the project hierarchy.</summary>
    public Maybe<SkeletalMeshAsset> FindAsset(MeshComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return Project.Bind(p => p.Find(component.AssetId)).Bind(i => Maybe.From(i as SkeletalMeshAsset));
    }

    /// <summary>World-space bounds of every mesh with geometry, or None if there is nothing to show.</summary>
    public Maybe<BoundingBox> Bounds()
    {
        var corners = new List<Vector3>();
        foreach (var mesh in Meshes)
        {
            if (FindAsset(mesh).Bind(a => a.Geometry).TryGetValue(out var geometry))
            {
                // The local transform is current even before a deferred graph has evaluated the world matrix.
                var world = mesh.Parent == Root ? mesh.LocalTransform.ToMatrix() : mesh.WorldMatrix;
                corners.Add(Vector3.Transform(geometry.Bounds.Min, world));
                corners.Add(Vector3.Transform(geometry.Bounds.Max, world));
            }
        }
        return corners.Count == 0 ? Maybe<BoundingBox>.None : BoundingBox.FromPoints(corners.ToArray());
    }

    /// <summary>Points the camera at everything in the scene.</summary>
    public void FrameAll()
    {
        if (Bounds().TryGetValue(out var bounds))
        {
            Camera.Frame(bounds.Center, bounds.Radius);
        }
    }

    /// <summary>Copies the camera and light settings into <paramref name="project"/> so they are saved with it.</summary>
    public void StoreViewSettings(CharacterProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Camera = Camera.Settings;
        project.Light = Light.Settings;
    }

    /// <summary>Selects a component and attaches a transform gizmo that follows it. Pass null to clear.</summary>
    public void Select(SceneComponent? component)
    {
        if (component == Selected)
        {
            return;
        }
        if (component is not null && component.Scene != this)
        {
            throw new InvalidOperationException($"{component.Name} is not part of this scene.");
        }

        using (Graph.Evaluator.BeginBatch())
        {
            if (Gizmo is not null)
            {
                Root.RemoveChild(Gizmo);
                Gizmo = null;
            }
            Selected = component;
            if (component is not null)
            {
                Gizmo = Root.AddChild(new GizmoComponent(component));
            }
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
