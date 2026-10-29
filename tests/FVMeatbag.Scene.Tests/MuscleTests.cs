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

    private static readonly Lazy<SkinSurface> Skin = new(() => SkinSurface.From(Demo.Value.Geometry));

    private static SkinSurface DemoSkin => Skin.Value;

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
        Assert.True(MuscleTemplate.Default.Find("Biceps_l").HasValue);
    }

    [Fact]
    public void Template_places_every_strand_under_the_demo_skin_and_fits_it()
    {
        var muscles = new MuscleSet();

        var report = MuscleTemplate.Default.Generate(DemoBody, DemoSkin, muscles);

        Assert.Empty(report.Missing);
        var strands = MuscleTemplate.Default.Muscles.Sum(m => m.Strands.Count);
        Assert.Equal(18, MuscleTemplate.Default.Muscles.Count);
        Assert.Equal(strands * 2, muscles.Count);
        Assert.Equal(muscles.Count, report.Added.Count);
        foreach (var muscle in muscles.Muscles)
        {
            foreach (var end in new[] { muscle.Origin, muscle.Insertion }.Concat(muscle.Hooks))
            {
                var p = DemoBody.RestPosition(end);
                // The Demo's pelvis joint sits at the crotch, so strands starting there (hamstrings,
                // adductors) start on the skin rather than under it.
                Assert.True(Inside(p) || DemoSkin.Distance(p).Value < 0.01f * DemoBody.Unit, $"{muscle.Name} has a point at {p} outside the skin.");
            }
            Assert.True(muscle.Width > 0f && muscle.Thickness > 0f, muscle.Name);
            Assert.Equal(MuscleShape.FitSize, muscle.Fit.Value.Count);
            Assert.All(muscle.Fit.Value, f => Assert.InRange(f, 0.2f, 1.6f));
            Assert.True(muscle.Template.HasValue);
            Assert.True(muscle.Drives.Count > 0 || muscle.Group.StartsWith("ErectorSpinae", StringComparison.Ordinal), muscle.Name);
        }
        Assert.Equal("Pectoral_l", muscles.Find("Pectoral2_l").Value.Group);
        Assert.Equal(new MuscleSource("Pectoral", 1), muscles.Find("Pectoral2_l").Value.Template.Value);
        Assert.Equal(2, muscles.Find("Gluteus_r").Value.Hooks.Count);
    }

    [Fact]
    public void Fitted_limb_muscles_stay_under_the_skin()
    {
        var muscles = new MuscleSet();
        MuscleTemplate.Default.Generate(DemoBody, DemoSkin, muscles);

        foreach (var name in new[] { "Biceps_l", "Triceps_r", "Quadriceps2_l", "Gastrocnemius1_r" })
        {
            var muscle = muscles.Find(name).Value;
            var origin = DemoBody.RestPosition(muscle.Origin);
            var insertion = DemoBody.RestPosition(muscle.Insertion);
            var facing = Vector3.TransformNormal(muscle.Facing, DemoSkeleton.WorldRest[DemoSkeleton.FindBone(muscle.Origin.Bone).Value]);
            var (x, y, z) = MuscleShape.Axes(origin, insertion, facing);
            var local = MuscleShape.LocalVertices(muscle, Vector3.Distance(origin, insertion));
            for (var i = 1; i < local.Length - 1; i++)
            {
                var center = origin + (local[i].Y * y);
                var radial = (local[i].X * x) + (local[i].Z * z);
                var room = DemoSkin.RayCast(center, Vector3.Normalize(radial));
                Assert.True(room.IsNone || radial.Length() <= room.Value + 1e-4f, $"{name} vertex {i} pokes {radial.Length() - room.Value} out of the skin.");
            }
        }
    }

    [Fact]
    public void Template_muscles_on_both_sides_are_mirror_images()
    {
        var muscles = new MuscleSet();
        MuscleTemplate.Default.Generate(DemoBody, DemoSkin, muscles);
        var body = DemoBody;

        foreach (var left in muscles.Muscles.Where(m => m.Side == BoneSide.Left))
        {
            var right = muscles.Find(BoneNames.Mirror(left.Name)).Value;
            Assert.Equal(BoneSide.Right, right.Side);
            Assert.Equal(BoneNames.Mirror(left.Group), right.Group);
            AssertNear(body.Reflect(body.RestPosition(left.Origin)), body.RestPosition(right.Origin), left.Name);
            AssertNear(body.Reflect(body.RestPosition(left.Insertion)), body.RestPosition(right.Insertion), left.Name);
            foreach (var (l, r) in left.Hooks.Zip(right.Hooks))
            {
                AssertNear(body.Reflect(body.RestPosition(l)), body.RestPosition(r), left.Name + " hook");
            }
            Assert.Equal(left.Width, right.Width);
        }
    }

    [Fact]
    public void Building_the_template_again_replaces_its_muscles_and_keeps_hand_made_ones()
    {
        var muscles = new MuscleSet();
        MuscleTemplate.Default.Generate(DemoBody, DemoSkin, muscles);
        var biceps = muscles.Find("Biceps_l").Value;
        muscles.Set(biceps with { Width = 0.5f });
        muscles.Set(biceps with { Name = "Brachialis_l", Template = Maybe<MuscleSource>.None });
        var count = muscles.Count;

        MuscleTemplate.Default.Generate(DemoBody, DemoSkin, muscles);

        Assert.Equal(count, muscles.Count);
        Assert.Equal(biceps.Width, muscles.Find("Biceps_l").Value.Width);
        Assert.True(muscles.Find("Brachialis_l").HasValue);
        var rebuilt = MuscleTemplate.Default.Rebuild(muscles.Find("Biceps_r").Value with { Width = 0.5f }, DemoBody, DemoSkin).Value;
        Assert.Equal(muscles.Find("Biceps_r").Value.Width, rebuilt.Width, 1e-6f);
        Assert.Equal("upperarm_r", rebuilt.Origin.Bone);
    }

    [Fact]
    public void Template_needs_the_mannequin_bones()
    {
        var skeleton = new Skeleton([new Bone("pelvis", -1, Matrix4x4.Identity), new Bone("spine_01", 0, Matrix4x4.CreateTranslation(0, 0.1f, 0))]);

        var missing = MuscleTemplate.Default.MissingBones(skeleton);

        Assert.Contains("head", missing);
        Assert.Contains("chest", missing);
        Assert.DoesNotContain("pelvis", missing);
        Assert.Empty(MuscleTemplate.Default.MissingBones(DemoSkeleton));
    }

    [Fact]
    public void Mirroring_an_edited_muscle_replaces_its_twin()
    {
        var muscles = new MuscleSet();
        MuscleTemplate.Default.Generate(DemoBody, DemoSkin, muscles);
        var template = muscles.Find("Biceps_r").Value;
        muscles.Set(muscles.Find("Biceps_l").Value with { Width = 0.05f, Volume = 1.5f });

        var twin = muscles.Mirror("Biceps_l", DemoBody, DemoSkin).Value;

        Assert.Equal("Biceps_r", twin.Name);
        Assert.Equal(twin, muscles.Find("Biceps_r").Value);
        Assert.Equal(0.05f, twin.Width);
        Assert.Equal(1.5f, twin.Volume);
        Assert.True(twin.Fit.HasValue);
        Assert.Equal("upperarm_r", twin.Origin.Bone);
        AssertNear(DemoBody.RestPosition(template.Origin), DemoBody.RestPosition(twin.Origin), "origin");
        AssertNear(DemoBody.RestPosition(template.Insertion), DemoBody.RestPosition(twin.Insertion), "insertion");
        muscles.Set(Muscle("Diaphragm", BoneSide.Center));
        Assert.True(muscles.Mirror("Diaphragm", DemoBody).IsNone); // center muscles have no twin
    }

    [Fact]
    public void Shortening_bulges_by_the_volume_setting()
    {
        var muscle = Muscle("m", BoneSide.Left) with { Volume = 1f };

        Assert.Equal(1f, muscle.CrossScale(1f), 1e-5f);
        Assert.Equal(MathF.Sqrt(2f), muscle.CrossScale(0.5f), 1e-4f);
        Assert.Equal(1f / MathF.Sqrt(2f), muscle.CrossScale(2f), 1e-4f);
        Assert.Equal(2f, (muscle with { Volume = 2f }).CrossScale(0.5f), 1e-4f);
        Assert.Equal(1f, (muscle with { Volume = 0f }).CrossScale(0.5f), 1e-5f);
        Assert.Equal(Core.Muscles.Muscle.MaxCrossScale, muscle.CrossScale(0.01f));
        Assert.Equal(Core.Muscles.Muscle.MinCrossScale, muscle.CrossScale(100f));
    }

    [Fact]
    public void Strand_is_widest_at_its_belly_and_wound_outwards()
    {
        var muscle = Muscle("m", BoneSide.Left) with { Belly = 0.3f, Tendon = 0.2f };

        Assert.Equal(1f, MuscleShape.RadiusProfile(0.3f, 0.3f, 0.2f), 1e-5f);
        Assert.Equal(0.2f, MuscleShape.RadiusProfile(0f, 0.3f, 0.2f), 1e-5f);
        Assert.Equal(0.2f, MuscleShape.RadiusProfile(1f, 0.3f, 0.2f), 1e-5f);
        var vertices = MuscleShape.LocalVertices(muscle, 2f);
        Assert.Equal(MuscleShape.VertexCount, vertices.Length);
        Assert.Equal(Vector3.Zero, vertices[0]);
        Assert.Equal(new Vector3(0, 2, 0), vertices[^1]);
        var triangles = MuscleShape.Triangles;
        Assert.Equal(MuscleShape.Segments * 2 * (MuscleShape.InnerRings - 1 + 1) * 3, triangles.Length);
        for (var i = 0; i < triangles.Length; i += 3)
        {
            var (a, b, c) = (vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]);
            var centroid = (a + b + c) / 3f;
            var outward = centroid with { Y = 0 };
            if (outward.LengthSquared() < 1e-8f)
            {
                outward = new Vector3(0, centroid.Y - 1f, 0);
            }
            Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), outward) > 0f, $"triangle {i / 3} faces inwards");
        }
    }

    [Fact]
    public void Many_muscles_drive_one_corrective_and_one_muscle_drives_many()
    {
        var biceps = Muscle("Biceps_l", BoneSide.Left) with
        {
            Drives = [new MuscleDrive("bicep", Maybe<string>.None, 1f), new MuscleDrive("elbow", Maybe<string>.None, 0.25f)],
        };
        var triceps = Muscle("Triceps_l", BoneSide.Left) with { Drives = [new MuscleDrive("elbow", "hi", 0.4f)] };
        var elbow = new CorrectiveTag("elbow", BoneSide.Left, Maybe<string>.None);

        Assert.Equal(1f, biceps.DriveWeight(new CorrectiveTag("bicep", BoneSide.Left, Maybe<string>.None)));
        Assert.Equal(0.25f, biceps.DriveWeight(elbow));
        Assert.Equal(0.25f, biceps.DriveWeight(elbow with { Part = "hi" })); // a drive without a part drives every part
        Assert.Equal(0f, biceps.DriveWeight(elbow with { Side = BoneSide.Right }));
        Assert.Equal(0.4f, triceps.DriveWeight(elbow with { Part = "hi" }));
        Assert.Equal(0f, triceps.DriveWeight(elbow));
        var abs = new CorrectiveTag("abdominals", BoneSide.Center, Maybe<string>.None);
        var rectus = Muscle("RectusAbdominis_r", BoneSide.Right) with { Drives = [new MuscleDrive("abdominals", Maybe<string>.None, 1f)] };
        Assert.Equal(1f, rectus.DriveWeight(abs)); // both sides drive a center corrective
    }

    [Fact]
    public void Muscles_are_saved_with_the_project()
    {
        var directory = _temp.Combine("Projects", "Hero");
        var project = new CharacterProject("Hero", directory);
        var path = ProjectFile.CopyIntoProject(project, SkeletonImportTests.SamplePath("Demo", "Meshes", "MannyBaseMesh.glb"));
        var asset = project.Meshes.Add(new SkeletalMeshAsset("Hero", path) { Geometry = Demo.Value.Geometry, Skeleton = Demo.Value.Skeleton });
        MuscleTemplate.Default.Generate(DemoBody, DemoSkin, asset.Muscles);
        asset.Muscles.Set(asset.Muscles.Find("Triceps_l").Value with
        {
            Volume = 0.5f,
            Offset = 0.01f,
            Fit = Maybe<IReadOnlyList<float>>.None,
            Drives = [new MuscleDrive("tricep", "hi", 0.75f)],
        });
        ProjectFile.Save(project);

        var problems = new List<string>();
        var loaded = Assert.Single(ProjectFile.Load(directory, problems).MeshAssets).Muscles;

        Assert.Empty(problems);
        Assert.Equal(asset.Muscles.Muscles.Select(m => m.Name), loaded.Muscles.Select(m => m.Name));
        foreach (var (saved, read) in asset.Muscles.Muscles.Zip(loaded.Muscles))
        {
            Assert.Equal(Plain(saved), Plain(read));
            Assert.Equal(saved.Drives, read.Drives);
            Assert.Equal(saved.Hooks, read.Hooks);
            Assert.Equal(saved.Fit.HasValue, read.Fit.HasValue);
            Assert.Equal(saved.Fit.Match(f => f, () => []), read.Fit.Match(f => f, () => []));
        }

        static Muscle Plain(Muscle m) => m with { Drives = [], Hooks = [], Fit = Maybe<IReadOnlyList<float>>.None };
    }

    [Fact]
    public void Muscles_saved_by_the_first_preview_are_left_out_with_a_note()
    {
        var directory = _temp.Combine("Projects", "Old");
        var project = new CharacterProject("Old", directory);
        var path = ProjectFile.CopyIntoProject(project, SkeletonImportTests.SamplePath("Demo", "Meshes", "MannyBaseMesh.glb"));
        var asset = project.Meshes.Add(new SkeletalMeshAsset("Hero", path) { Geometry = Demo.Value.Geometry, Skeleton = Demo.Value.Skeleton });
        asset.Muscles.Set(Muscle("Biceps_l", BoneSide.Left));
        ProjectFile.Save(project);
        var file = Path.Combine(directory, ProjectFile.FileName);
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"belly\"", "\"radius\"", StringComparison.Ordinal));

        var problems = new List<string>();
        var loaded = Assert.Single(ProjectFile.Load(directory, problems).MeshAssets).Muscles;

        Assert.Equal(0, loaded.Count);
        Assert.Contains(problems, p => p.Contains("older version", StringComparison.Ordinal));
    }

    [Fact]
    public void Renaming_a_bone_keeps_its_muscles_attached()
    {
        var asset = new SkeletalMeshAsset("Hero") { Geometry = Demo.Value.Geometry, Skeleton = GltfImporter.Load(SkeletonImportTests.SamplePath("Demo", "Meshes", "MannyBaseMesh.glb")).Skeleton };
        MuscleTemplate.Default.Generate(BodyFrame.From(asset.Skeleton.Value).Value, DemoSkin, asset.Muscles);
        var bone = asset.Skeleton.Value.FindBone("lowerarm_l").Value;

        Assert.True(asset.RenameBone(bone, "forearm_bone_l"));

        Assert.Equal("forearm_bone_l", asset.Muscles.Find("Biceps_l").Value.Insertion.Bone);
        Assert.DoesNotContain(asset.Muscles.Muscles, m => m.Origin.Bone == "lowerarm_l" || m.Insertion.Bone == "lowerarm_l");
    }

    [Fact]
    public void Scene_builds_a_preview_mesh_per_muscle_and_highlights_the_selected_one()
    {
        var (root, scene, asset) = ShowDemo();
        var muscles = Assert.Single(scene.MuscleComponents);
        Assert.Same(scene.Skeletons.Single(), muscles.Parent);

        muscles.SelectedMuscle = asset.Muscles.IndexOf("Biceps_l");
        root.Evaluator.Evaluate();

        var meshes = muscles.ShapeNode!.OutputValues.Meshes.ToArray();
        Assert.Empty(muscles.RigNode!.OutputValues.Problems);
        Assert.Equal(asset.Muscles.Count, meshes.Length);
        Assert.Equal(asset.Muscles.Count, meshes.Select(m => m.Id).Distinct().Count());
        Assert.Equal(asset.Muscles.IndexOf("Biceps_l").Value, Assert.Single(meshes, m => m.Selected).Index);
        foreach (var mesh in meshes)
        {
            var vertices = mesh.Positions.Length;
            Assert.Equal(MuscleShape.VertexCount, vertices);
            Assert.Equal(asset.Muscles.Muscles[mesh.Index].Color, mesh.Color);
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
        var rest = Frame(muscles, asset, "Biceps_l");
        Assert.Equal(1f, rest.Stretch, 1e-4f);
        Assert.Equal(1f, rest.CrossScale, 1e-4f);

        var elbow = skeleton.Skeleton.FindBone("lowerarm_l").Value;
        skeleton.RotateBone(elbow, skeleton.Pose[elbow], Vector3.UnitZ, float.DegreesToRadians(100));
        root.Evaluator.Evaluate();

        var biceps = Frame(muscles, asset, "Biceps_l");
        var triceps = Frame(muscles, asset, "Triceps_l");
        Assert.True(biceps.Stretch < 0.95f, $"biceps stretch {biceps.Stretch}");
        Assert.True(biceps.CrossScale > 1.02f, $"biceps bulge {biceps.CrossScale}");
        Assert.True(triceps.Stretch > 1.02f, $"triceps stretch {triceps.Stretch}");
        Assert.Equal(1f, Frame(muscles, asset, "Biceps_r").Stretch, 1e-4f); // the other arm did not move
        // The biceps' ends follow their bones.
        var bones = skeleton.PoseNode!.OutputValues.BoneWorld.Span;
        var insertion = asset.Muscles.Find("Biceps_l").Value.Insertion;
        AssertNear(Vector3.Transform(insertion.Local, bones[elbow]), biceps.Insertion, "biceps insertion");
    }

    [Fact]
    public void Editing_a_muscle_updates_its_preview()
    {
        var (root, scene, asset) = ShowDemo();
        var muscles = scene.MuscleComponents.Single();
        root.Evaluator.Evaluate();
        var index = asset.Muscles.IndexOf("Biceps_l").Value;
        var before = Width(muscles.ShapeNode!.OutputValues.Meshes.Span[index], Frame(muscles, asset, "Biceps_l"));

        var biceps = asset.Muscles.Muscles[index];
        asset.Muscles.Set(biceps with { Width = biceps.Width * 2, Thickness = biceps.Thickness * 2 });
        root.Evaluator.Evaluate();

        Assert.Equal(before * 2, Width(muscles.ShapeNode.OutputValues.Meshes.Span[index], Frame(muscles, asset, "Biceps_l")), before * 0.05f);

        asset.Muscles.Remove("Biceps_l");
        root.Evaluator.Evaluate();

        Assert.Equal(asset.Muscles.Count, muscles.ShapeNode.OutputValues.Meshes.Length);
    }

    [Fact]
    public void Hooks_bend_the_gluteus_with_the_thigh()
    {
        var (root, scene, asset) = ShowDemo();
        var muscles = scene.MuscleComponents.Single();
        var skeleton = scene.Skeletons.Single();
        root.Evaluator.Evaluate();
        var index = asset.Muscles.IndexOf("Gluteus_l").Value;
        var resolved = muscles.RigNode!.OutputValues.Muscles.ToArray().Single(m => m.Index == index);
        var unhooked = resolved with { Hooks = ReadOnlyMemory<Operations.ResolvedHook>.Empty };
        // At rest the hooks change nothing.
        var rest = muscles.ShapeNode!.OutputValues.Meshes.Span[index].Positions.ToArray();
        var (plain, _) = Operations.MuscleShapeOperation.Build(unhooked, Frame(muscles, asset, "Gluteus_l"));
        Assert.All(rest.Zip(plain), p => AssertNear(p.Second, p.First, "rest vertex"));

        var thigh = skeleton.Skeleton.FindBone("thigh_l").Value;
        skeleton.RotateBone(thigh, skeleton.Pose[thigh], Vector3.UnitZ, float.DegreesToRadians(60));
        root.Evaluator.Evaluate();

        var bent = muscles.ShapeNode.OutputValues.Meshes.Span[index].Positions.ToArray();
        (plain, _) = Operations.MuscleShapeOperation.Build(unhooked, Frame(muscles, asset, "Gluteus_l"));
        var hooks = resolved.Hooks.ToArray();
        Assert.Equal(["spine_01", "thigh_l"], hooks.Select(h => skeleton.Skeleton.Bones[h.Bone].Name));
        var moved = 0;
        for (var v = 0; v < bent.Length; v++)
        {
            var offset = Vector3.Distance(plain[v], bent[v]);
            if (hooks.All(h => h.Weights.Span[v] == 0f))
            {
                Assert.True(offset < 1e-5f, $"vertex {v} out of the hooks' reach moved {offset}");
            }
            else if (hooks[1].Weights.Span[v] > 0.5f && offset > 1e-3f)
            {
                moved++;
            }
        }
        Assert.True(moved > 0, "no vertex near the thigh hook followed the thigh");
    }

    private (Graph.Graph Root, SceneRoot Scene, SkeletalMeshAsset Asset) ShowDemo()
    {
        var project = new CharacterProject("Hero", _temp.Combine("Hero"));
        var asset = project.Meshes.Add(new SkeletalMeshAsset("Hero") { Geometry = Demo.Value.Geometry, Skeleton = Demo.Value.Skeleton });
        MuscleTemplate.Default.Generate(DemoBody, DemoSkin, asset.Muscles);
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
        name,
        Maybe<MuscleSource>.None,
        side,
        new MuscleAttachment("upperarm_l", Vector3.Zero),
        new MuscleAttachment("lowerarm_l", Vector3.Zero),
        [],
        Vector3.UnitX,
        0.1f,
        0.1f,
        Core.Muscles.Muscle.DefaultBelly,
        Core.Muscles.Muscle.DefaultTendon,
        1f,
        0f,
        Maybe<IReadOnlyList<float>>.None,
        new Vector3(0.75f, 0.2f, 0.2f),
        []);

    /// <summary>A point is inside the closed skin when rays along all six axes meet it.</summary>
    private static bool Inside(Vector3 point) =>
        new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ }.All(d => DemoSkin.RayCast(point, d).HasValue);

    private static void AssertNear(Vector3 expected, Vector3 actual, string what) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-3f, $"{what}: expected {expected}, got {actual}.");
}
