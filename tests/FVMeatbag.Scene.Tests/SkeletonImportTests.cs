using System.Numerics;
using FVMeatbag.Core.Import;

namespace FVMeatbag.Scene.Tests;

public sealed class SkeletonImportTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Static_meshes_have_no_skeleton()
    {
        var mesh = GltfImporter.Load(TestMeshes.WriteCube(_temp.Combine("Cube.glb")));

        Assert.True(mesh.Skeleton.IsNone);
        Assert.True(mesh.Geometry.Skin.IsNone);
    }

    [Fact]
    public void Skinned_mesh_is_deformed_into_the_rest_pose_including_the_armature_transform()
    {
        var offset = new Vector3(5, 0, 0);
        var mesh = GltfImporter.Load(TestSkinnedMeshes.WriteBentArm(_temp.Combine("Arm.glb"), offset));

        var skeleton = mesh.Skeleton.Value;
        Assert.Equal(["Lower", "Upper"], skeleton.Bones.Select(b => b.Name));
        Assert.Equal(-1, skeleton.Bones[0].Parent);
        Assert.Equal(0, skeleton.Bones[1].Parent);
        AssertNear(offset + Vector3.UnitY, skeleton.WorldRest[1].Translation);

        // The upper half was bent 90° around Z at the elbow, so its tip (0, 2, 0) ends up at (-1, 1, 0) + offset.
        var positions = mesh.Geometry.Positions.ToArray();
        Assert.Contains(positions, p => Vector3.Distance(p, offset + new Vector3(-1f, 1.1f, -0.1f)) < 1e-4f);
        Assert.Equal(new Vector3(-1f, 0, -0.1f) + offset, mesh.Geometry.Bounds.Min, Near);
        Assert.Equal(new Vector3(0.1f, 1.1f, 0.1f) + offset, mesh.Geometry.Bounds.Max, Near);
    }

    [Fact]
    public void Skin_weights_point_at_the_right_bones()
    {
        var mesh = GltfImporter.Load(TestSkinnedMeshes.WriteBentArm(_temp.Combine("Arm.glb"), Vector3.Zero));

        var positions = mesh.Geometry.Positions.Span;
        var skin = mesh.Geometry.Skin.Value.Span;
        for (var i = 0; i < positions.Length; i++)
        {
            // Lower-half vertices keep x = ±0.1; the bent upper half lies along -x, from the elbow (x = 0) to x = -1.
            var expectedBone = MathF.Abs(MathF.Abs(positions[i].X) - 0.1f) < 1e-4f ? 0 : 1;
            Assert.Equal((expectedBone, 1f), skin[i].Influences().Single());
        }
    }

    [Fact]
    public void The_sample_character_imports_upright_with_its_metarig()
    {
        var mesh = GltfImporter.Load(SamplePath("MaleBaseMesh", "MaleBaseMesh.glb"));

        var skeleton = mesh.Skeleton.Value;
        Assert.Equal(56, skeleton.Count);
        Assert.True(skeleton.FindBone("spine").HasValue);
        Assert.True(skeleton.FindBone("hand.L").HasValue);
        var size = mesh.Geometry.Bounds.Size;
        Assert.True(size.Y > size.Z && size.Y > 1.5f && size.Y < 2.2f, $"Expected a standing adult in meters, got {size}.");
        Assert.All(mesh.Geometry.Skin.Value.ToArray(), w =>
        {
            var total = w.Weights.X + w.Weights.Y + w.Weights.Z + w.Weights.W;
            Assert.True(MathF.Abs(total - 1f) < 1e-4f || total == 0f);
            Assert.All(w.Influences(), i => Assert.InRange(i.Bone, 0, skeleton.Count - 1));
        });
        // Bones sit inside the body.
        var bounds = mesh.Geometry.Bounds;
        Assert.All(skeleton.WorldRest, m => Assert.True(
            Vector3.Clamp(m.Translation, bounds.Min - new Vector3(0.05f), bounds.Max + new Vector3(0.05f)) == m.Translation,
            $"Bone at {m.Translation} is outside the mesh bounds {bounds}."));
    }

    internal static string SamplePath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "assets", "Samples")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        return Path.Combine([directory.FullName, "assets", "Samples", .. parts]);
    }

    private static readonly IEqualityComparer<Vector3> Near =
        EqualityComparer<Vector3>.Create((a, b) => Vector3.Distance(a, b) < 1e-4f, _ => 0);

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}.");
}
