using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Muscles;
using FVMeatbag.Core.Project;
using FVMeatbag.Core.Rig;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Components;

namespace FVMeatbag.Scene.Tests;

/// <summary>The muscle preview: template placement on the Demo rig, editing, saving, and the graph that poses it.</summary>
public sealed class MuscleTests : IDisposable
{
    private static readonly Lazy<LoadedMesh> Demo = new(() => GltfImporter.Load(SkeletonImportTests.SamplePath("Demo", "Meshes", "MannyBaseMesh.glb")));
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static Skeleton DemoSkeleton => Demo.Value.Skeleton.Value;

    private static BodyFrame DemoBody => BodyFrame.From(DemoSkeleton).Value;

    [Fact]
    public void Body_frame_reads_the_demo_characters_directions()
    {
        var body = DemoBody;

        // The Demo character is Y-up and faces +X, so its left is -Z.
        Assert.True(Vector3.Dot(body.Up, Vector3.UnitY) > 0.99f);
        Assert.True(Vector3.Dot(body.Forward, Vector3.UnitX) > 0.99f);
        Assert.True(Vector3.Dot(body.Left, -Vector3.UnitZ) > 0.99f);
        Assert.InRange(body.Unit, 0.5f, 1.2f);
    }

    [Fact]
    public void Body_frame_needs_the_manny_landmarks()
    {
        var skeleton = new Skeleton([new Bone("pelvis", -1, Matrix4x4.Identity)]);

        Assert.True(BodyFrame.From(skeleton).IsNone);
        Assert.True(MuscleTemplate.Default.Find("biceps").HasValue);
    }

    [Fact]
    public void Template_places_every_muscle_inside_the_demo_body()
    {
        var muscles = new MuscleSet();

        var report = MuscleTemplate.Default.Apply(DemoBody, muscles);

        Assert.Empty(report.Skipped);
        var sided = MuscleTemplate.Default.Entries.Count(e => e.Sided);
        Assert.Equal((sided * 2) + (MuscleTemplate.Default.Entries.Count - sided), muscles.Count);
        var bounds = Demo.Value.Geometry.Bounds;
        foreach (var muscle in muscles.Muscles)
        {
            foreach (var end in new[] { muscle.Origin, muscle.Insertion })
            {
                var p = DemoBody.RestPosition(end);
                Assert.True(Vector3.Clamp(p, bounds.Min, bounds.Max) == p, $"{muscle.Name} has an end at {p}, outside {bounds}.");
            }
            Assert.True(muscle.Radius > 0f);
            Assert.NotEmpty(muscle.Drives);
        }
    }

    [Fact]
    public void Template_muscles_on_both_sides_are_mirror_images()
    {
        var muscles = new MuscleSet();
        MuscleTemplate.Default.Apply(DemoBody, muscles);
        var body = DemoBody;

        foreach (var left in muscles.Muscles.Where(m => m.Side == BoneSide.Left))
        {
            var right = muscles.Find(BoneNames.Mirror(left.Name)).Value;
            Assert.Equal(BoneSide.Right, right.Side);
            AssertNear(body.Reflect(body.RestPosition(left.Origin)), body.RestPosition(right.Origin), left.Name);
            AssertNear(body.Reflect(body.RestPosition(left.Insertion)), body.RestPosition(right.Insertion), left.Name);
        }
    }

    [Fact]
    public void Applying_the_template_again_keeps_edited_muscles()
    {
        var muscles = new MuscleSet();
        MuscleTemplate.Default.Apply(DemoBody, muscles);
        var biceps = muscles.Find("biceps_l").Value;
        muscles.Set(biceps with { Radius = 0.5f });

        var report = MuscleTemplate.Default.Apply(DemoBody, muscles);

        Assert.Empty(report.Added);
        Assert.Equal(muscles.Count, report.Skipped.Count);
        Assert.Equal(0.5f, muscles.Find("biceps_l").Value.Radius);
    }

    [Fact]
    public void Mirroring_an_edited_muscle_replaces_its_twin()
    {
        var muscles = new MuscleSet();
        MuscleTemplate.Default.Apply(DemoBody, muscles);
        var template = muscles.Find("biceps_r").Value;
        muscles.Set(muscles.Find("biceps_l").Value with { Radius = 0.05f, Volume = 1.5f });

        var twin = muscles.Mirror("biceps_l", DemoBody).Value;

        Assert.Equal("biceps_r", twin.Name);
        Assert.Equal(twin, muscles.Find("biceps_r").Value);
        Assert.Equal(0.05f, twin.Radius);
        Assert.Equal(1.5f, twin.Volume);
        Assert.Equal("upperarm_r", twin.Origin.Bone);
        AssertNear(DemoBody.RestPosition(template.Origin), DemoBody.RestPosition(twin.Origin), "origin");
        AssertNear(DemoBody.RestPosition(template.Insertion), DemoBody.RestPosition(twin.Insertion), "insertion");
        Assert.True(muscles.Mirror("rectus_abdominis", DemoBody).IsNone); // center muscles have no twin
    }

    [Fact]
    public void Shortening_bulges_flexors_and_thins_extensors()
    {
        var flexor = Muscle("m", BoneSide.Left) with { Volume = 1f };
        var extensor = flexor with { Action = MuscleAction.Extensor };

        Assert.Equal(1f, flexor.CrossScale(1f), 1e-5f);
        Assert.Equal(MathF.Sqrt(2f), flexor.CrossScale(0.5f), 1e-4f);
        Assert.Equal(1f / MathF.Sqrt(2f), extensor.CrossScale(0.5f), 1e-4f);
        Assert.Equal(1f, (flexor with { Volume = 0f }).CrossScale(0.5f), 1e-5f);
        Assert.Equal(Core.Muscles.Muscle.MaxCrossScale, flexor.CrossScale(0.01f));
        Assert.Equal(Core.Muscles.Muscle.MinCrossScale, flexor.CrossScale(100f));
    }

    [Fact]
    public void Many_muscles_drive_one_corrective_and_one_muscle_drives_many()
    {
        var biceps = Muscle("biceps_l", BoneSide.Left) with
        {
            Drives = [new MuscleDrive("bicep", Maybe<string>.None, 1f), new MuscleDrive("elbow", Maybe<string>.None, 0.25f)],
        };
        var triceps = Muscle("triceps_l", BoneSide.Left) with { Drives = [new MuscleDrive("elbow", "hi", 0.4f)] };
        var elbow = new CorrectiveTag("elbow", BoneSide.Left, Maybe<string>.None);

        Assert.Equal(1f, biceps.DriveWeight(new CorrectiveTag("bicep", BoneSide.Left, Maybe<string>.None)));
        Assert.Equal(0.25f, biceps.DriveWeight(elbow));
        Assert.Equal(0.25f, biceps.DriveWeight(elbow with { Part = "hi" })); // a drive without a part drives every part
        Assert.Equal(0f, biceps.DriveWeight(elbow with { Side = BoneSide.Right }));
        Assert.Equal(0.4f, triceps.DriveWeight(elbow with { Part = "hi" }));
        Assert.Equal(0f, triceps.DriveWeight(elbow));
    }

    [Fact]
    public void Muscles_are_saved_with_the_project()
    {
        var directory = _temp.Combine("Projects", "Hero");
        var project = new CharacterProject("Hero", directory);
        var path = ProjectFile.CopyIntoProject(project, SkeletonImportTests.SamplePath("Demo", "Meshes", "MannyBaseMesh.glb"));
        var asset = project.Meshes.Add(new SkeletalMeshAsset("Hero", path) { Geometry = Demo.Value.Geometry, Skeleton = Demo.Value.Skeleton });
        MuscleTemplate.Default.Apply(DemoBody, asset.Muscles);
        asset.Muscles.Set(asset.Muscles.Find("triceps_l").Value with
        {
            Action = MuscleAction.Extensor,
            Drives = [new MuscleDrive("tricep", "hi", 0.75f)],
        });
        ProjectFile.Save(project);

        var problems = new List<string>();
        var loaded = Assert.Single(ProjectFile.Load(directory, problems).MeshAssets).Muscles;

        Assert.Empty(problems);
        Assert.Equal(asset.Muscles.Muscles.Select(m => m.Name), loaded.Muscles.Select(m => m.Name));
        foreach (var (saved, read) in asset.Muscles.Muscles.Zip(loaded.Muscles))
        {
            Assert.Equal(saved with { Drives = [] }, read with { Drives = [] });
            Assert.Equal(saved.Drives, read.Drives);
        }
    }

    [Fact]
    public void Renaming_a_bone_keeps_its_muscles_attached()
    {
        var asset = new SkeletalMeshAsset("Hero") { Geometry = Demo.Value.Geometry, Skeleton = GltfImporter.Load(SkeletonImportTests.SamplePath("Demo", "Meshes", "MannyBaseMesh.glb")).Skeleton };
        MuscleTemplate.Default.Apply(BodyFrame.From(asset.Skeleton.Value).Value, asset.Muscles);
        var bone = asset.Skeleton.Value.FindBone("lowerarm_l").Value;

        Assert.True(asset.RenameBone(bone, "forearm_bone_l"));

        Assert.Equal("forearm_bone_l", asset.Muscles.Find("biceps_l").Value.Insertion.Bone);
        Assert.DoesNotContain(asset.Muscles.Muscles, m => m.Origin.Bone == "lowerarm_l" || m.Insertion.Bone == "lowerarm_l");
    }

    [Fact]
    public void Scene_builds_a_preview_mesh_per_muscle_and_highlights_the_selected_one()
    {
        var (root, scene, asset) = ShowDemo();
        var muscles = Assert.Single(scene.MuscleComponents);
        Assert.Same(scene.Skeletons.Single(), muscles.Parent);

        muscles.SelectedMuscle = asset.Muscles.IndexOf("biceps_l");
        root.Evaluator.Evaluate();

        var meshes = muscles.ShapeNode!.OutputValues.Meshes.ToArray();
        Assert.Empty(muscles.RigNode!.OutputValues.Problems);
        Assert.Equal(asset.Muscles.Count, meshes.Length);
        Assert.Equal(asset.Muscles.Count, meshes.Select(m => m.Id).Distinct().Count());
        Assert.Equal(asset.Muscles.IndexOf("biceps_l").Value, Assert.Single(meshes, m => m.Selected).Index);
        foreach (var mesh in meshes)
        {
            var vertices = mesh.Positions.Length;
            Assert.Equal(vertices, mesh.Normals.Length);
            Assert.Equal(0, mesh.Indices.Length % 3);
            Assert.All(mesh.Indices.ToArray(), i => Assert.InRange(i, 0u, (uint)vertices - 1));
            Assert.All(mesh.Normals.ToArray(), n => Assert.Equal(1f, n.Length(), 1e-3f));
        }

        scene.ShowMuscles = false;
        root.Evaluator.Evaluate();

        Assert.True(muscles.ShapeNode.OutputValues.Meshes.IsEmpty);
    }

    [Fact]
    public void Bending_the_elbow_shortens_and_bulges_the_biceps_and_stretches_the_triceps()
    {
        var (root, scene, asset) = ShowDemo();
        var muscles = scene.MuscleComponents.Single();
        var skeleton = scene.Skeletons.Single();
        root.Evaluator.Evaluate();
        var rest = Frame(muscles, asset, "biceps_l");
        Assert.Equal(1f, rest.Stretch, 1e-4f);
        Assert.Equal(1f, rest.CrossScale, 1e-4f);

        var elbow = skeleton.Skeleton.FindBone("lowerarm_l").Value;
        skeleton.RotateBone(elbow, skeleton.Pose[elbow], Vector3.UnitZ, float.DegreesToRadians(100));
        root.Evaluator.Evaluate();

        var biceps = Frame(muscles, asset, "biceps_l");
        var triceps = Frame(muscles, asset, "triceps_l");
        Assert.True(biceps.Stretch < 0.95f, $"biceps stretch {biceps.Stretch}");
        Assert.True(biceps.CrossScale > 1.02f, $"biceps bulge {biceps.CrossScale}");
        Assert.True(triceps.Stretch > 1.02f, $"triceps stretch {triceps.Stretch}");
        Assert.Equal(1f, Frame(muscles, asset, "biceps_r").Stretch, 1e-4f); // the other arm did not move
        // The biceps' ends follow their bones.
        var bones = skeleton.PoseNode!.OutputValues.BoneWorld.Span;
        var insertion = asset.Muscles.Find("biceps_l").Value.Insertion;
        AssertNear(Vector3.Transform(insertion.Local, bones[elbow]), biceps.Insertion, "biceps insertion");
    }

    [Fact]
    public void Editing_a_muscle_updates_its_preview()
    {
        var (root, scene, asset) = ShowDemo();
        var muscles = scene.MuscleComponents.Single();
        root.Evaluator.Evaluate();
        var index = asset.Muscles.IndexOf("biceps_l").Value;
        var before = Width(muscles.ShapeNode!.OutputValues.Meshes.Span[index], Frame(muscles, asset, "biceps_l"));

        asset.Muscles.Set(asset.Muscles.Muscles[index] with { Radius = asset.Muscles.Muscles[index].Radius * 2 });
        root.Evaluator.Evaluate();

        Assert.Equal(before * 2, Width(muscles.ShapeNode.OutputValues.Meshes.Span[index], Frame(muscles, asset, "biceps_l")), before * 0.05f);

        asset.Muscles.Remove("biceps_l");
        root.Evaluator.Evaluate();

        Assert.Equal(asset.Muscles.Count, muscles.ShapeNode.OutputValues.Meshes.Length);
    }

    private (Graph.Graph Root, SceneRoot Scene, SkeletalMeshAsset Asset) ShowDemo()
    {
        var project = new CharacterProject("Hero", _temp.Combine("Hero"));
        var asset = project.Meshes.Add(new SkeletalMeshAsset("Hero") { Geometry = Demo.Value.Geometry, Skeleton = Demo.Value.Skeleton });
        MuscleTemplate.Default.Apply(DemoBody, asset.Muscles);
        var root = Graph.Graph.CreateRoot();
        var scene = new SceneRoot(root);
        scene.Show(project);
        return (root, scene, asset);
    }

    private static Operations.MuscleFrame Frame(MuscleComponent muscles, SkeletalMeshAsset asset, string name)
    {
        var index = asset.Muscles.IndexOf(name).Value;
        var resolved = muscles.RigNode!.OutputValues.Muscles.Span;
        for (var i = 0; i < resolved.Length; i++)
        {
            if (resolved[i].Index == index)
            {
                return muscles.FrameNode!.OutputValues.Frames.Span[i];
            }
        }
        throw new InvalidOperationException($"{name} is not resolved.");
    }

    /// <summary>How far the mesh reaches from the muscle's axis: the belly's half-width.</summary>
    private static float Width(Operations.MuscleMesh mesh, Operations.MuscleFrame frame)
    {
        var direction = Vector3.Normalize(frame.Insertion - frame.Origin);
        var widest = 0f;
        foreach (var p in mesh.Positions.Span)
        {
            var offset = p - frame.Origin;
            widest = MathF.Max(widest, (offset - (Vector3.Dot(offset, direction) * direction)).Length());
        }
        return widest;
    }

    private static Muscle Muscle(string name, BoneSide side) => new(
        name,
        Maybe<string>.None,
        side,
        new MuscleAttachment("a", Vector3.Zero),
        new MuscleAttachment("b", Vector3.UnitY),
        Vector3.UnitX,
        MuscleShape.Fusiform,
        MuscleAction.Flexor,
        0.1f,
        1f,
        0f,
        1f,
        new Vector2(0.1f, 0.1f),
        []);

    private static void AssertNear(Vector3 expected, Vector3 actual, string what) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-3f, $"{what}: expected {expected}, got {actual}.");
}
