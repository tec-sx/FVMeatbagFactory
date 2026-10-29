using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Rig;

namespace FVMeatbag.Scene.Tests;

/// <summary>The Demo character, re-rigged to the UE5 Manny skeleton by tools/MannyRerig.</summary>
public sealed class DemoRigTests
{
    private static readonly Lazy<LoadedMesh> Demo = new(() => GltfImporter.Load(SkeletonImportTests.SamplePath("Demo", "Meshes", "MannyBaseMesh.glb")));
    private static readonly Lazy<LoadedMesh> Original = new(() => GltfImporter.Load(FbxImportTests.FixturePath("MaleBaseMesh.glb")));

    [Fact]
    public void Demo_is_a_manny_skeleton_with_sample_correctives()
    {
        var check = MannyRig.Check(Demo.Value.Skeleton.Value);

        Assert.True(check.IsConformant, string.Join("\n", check.Problems.Select(p => p.Message)));
        var rig = check.Rig.Value;
        Assert.Equal(MannySkeleton.Bones.Count(b => !b.Name.StartsWith("ik_", StringComparison.Ordinal) && b.Name is not ("interaction" or "center_of_mass")), rig.CoreCount);
        Assert.Equal(26, rig.Extras.Count);
    }

    [Fact]
    public void Every_sample_corrective_gets_a_role_from_its_name()
    {
        var rig = MannyRig.Check(Demo.Value.Skeleton.Value).Rig.Value;
        var setup = new CorrectiveSetup();

        Assert.Equal(rig.Extras.Count, setup.SuggestAll(rig, CorrectiveRoleCatalog.Default));
        foreach (var bone in rig.Extras)
        {
            var name = rig.Skeleton.Bones[bone].Name;
            Assert.Equal(name, setup[name].Tag.Value.CanonicalName);
        }
    }

    [Fact]
    public void Correctives_sit_inside_the_body_under_their_usual_parent()
    {
        var mesh = Demo.Value;
        var rig = MannyRig.Check(mesh.Skeleton.Value).Rig.Value;
        var bounds = mesh.Geometry.Bounds;
        foreach (var bone in rig.Extras)
        {
            var name = rig.Skeleton.Bones[bone].Name;
            var role = CorrectiveRoleCatalog.Default.Find(CorrectiveRoleCatalog.Default.Suggest(name).Value.Role).Value;
            var parent = rig.Skeleton.Bones[rig.Skeleton.Bones[bone].Parent].Name;
            Assert.Equal(role.Parent, MannySkeleton.BaseName(parent));
            var p = rig.Skeleton.WorldRest[bone].Translation;
            Assert.True(Vector3.Clamp(p, bounds.Min, bounds.Max) == p, $"{name} at {p} is outside {bounds}.");
        }
    }

    [Fact]
    public void Rest_shape_and_weight_totals_match_the_original()
    {
        var demo = Demo.Value.Geometry;
        var original = Original.Value.Geometry;

        Assert.Equal(original.Bounds.Min, demo.Bounds.Min, Near);
        Assert.Equal(original.Bounds.Max, demo.Bounds.Max, Near);
        Assert.Equal(original.TriangleCount, demo.TriangleCount);
        Assert.All(demo.Skin.Value.ToArray(), w => Assert.Equal(1f, w.TotalWeight, 1e-4f));
    }

    [Theory]
    [InlineData("forearm.L", "lowerarm_l")]
    [InlineData("shin.R", "calf_r")]
    [InlineData("hand.L", "hand_l")]
    [InlineData("spine.003", "spine_05")]
    public void Bending_a_joint_deforms_like_the_original_rig(string originalBone, string mannyBone)
    {
        var bend = Quaternion.CreateFromAxisAngle(Vector3.UnitX, float.DegreesToRadians(60));

        var expected = Bent(Original.Value, originalBone, bend);
        var actual = Bent(Demo.Value, mannyBone, bend);

        // Vertices are matched by position at rest: the re-rigged file may order them differently.
        var restExpected = Original.Value.Geometry.Positions.ToArray();
        var restActual = Demo.Value.Geometry.Positions.ToArray();
        var worst = 0f;
        for (var i = 0; i < restActual.Length; i++)
        {
            var match = Enumerable.Range(0, restExpected.Length).MinBy(j => Vector3.DistanceSquared(restExpected[j], restActual[i]));
            worst = MathF.Max(worst, Vector3.Distance(expected[match], actual[i]));
        }
        Assert.True(worst < 0.01f, $"Worst vertex is {worst * 100:0.##} cm away from the original rig's.");
    }

    /// <summary>Linear blend skinning with one bone turned about its own X axis.</summary>
    private static Vector3[] Bent(LoadedMesh mesh, string bone, Quaternion bend)
    {
        var pose = new SkeletonPose(mesh.Skeleton.Value);
        var index = mesh.Skeleton.Value.FindBone(bone).Value;
        var rest = pose.Rest[index];
        pose.Set(index, rest with { Rotation = Quaternion.Concatenate(bend, rest.Rotation) });
        var skin = pose.ComputeSkinMatrices(pose.ComputeWorld());
        var positions = mesh.Geometry.Positions.Span;
        var weights = mesh.Geometry.Skin.Value.Span;
        var result = new Vector3[positions.Length];
        for (var v = 0; v < result.Length; v++)
        {
            var blended = default(Matrix4x4);
            foreach (var (b, w) in weights[v].Influences())
            {
                blended += skin[b] * w;
            }
            result[v] = Vector3.Transform(positions[v], blended);
        }
        return result;
    }

    private static readonly IEqualityComparer<Vector3> Near =
        EqualityComparer<Vector3>.Create((a, b) => Vector3.Distance(a, b) < 1e-4f, v => 0);
}
