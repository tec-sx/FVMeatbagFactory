using System.Numerics;
using FVMeatbag.Core.Import;

namespace FVMeatbag.Scene.Tests;

/// <summary>The Demo character exported from Blender as FBX must import like the glTF original.</summary>
public sealed class FbxImportTests : IDisposable
{
    private const float Tolerance = 1e-3f; // a millimeter
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<string> Exports => ["MaleBaseMesh.blender-defaults.fbx", "MaleBaseMesh.zup-scale-all.fbx"];

    [Theory]
    [MemberData(nameof(Exports))]
    public void Fbx_matches_the_gltf_character_in_meters_and_y_up(string file)
    {
        var gltf = Original();
        var fbx = FbxImporter.Load(FixturePath(file));

        Assert.Equal(Path.GetFileNameWithoutExtension(file), fbx.Name);
        AssertNear(gltf.Geometry.Bounds.Min, fbx.Geometry.Bounds.Min);
        AssertNear(gltf.Geometry.Bounds.Max, fbx.Geometry.Bounds.Max);
        Assert.Equal(gltf.Geometry.Indices.Length, fbx.Geometry.Indices.Length);
    }

    [Theory]
    [MemberData(nameof(Exports))]
    public void Fbx_bones_sit_where_the_gltf_bones_do(string file)
    {
        var gltf = Original().Skeleton.Value;
        var fbx = FbxImporter.Load(FixturePath(file)).Skeleton.Value;

        foreach (var (bone, index) in gltf.Bones.Select((b, i) => (b, i)))
        {
            var match = fbx.FindBone(bone.Name);
            Assert.True(match.HasValue, $"{bone.Name} is missing.");
            AssertNear(gltf.WorldRest[index].Translation, fbx.WorldRest[match.Value].Translation);
            var parent = fbx.Bones[match.Value].Parent;
            Assert.Equal(bone.Parent < 0 ? "" : gltf.Bones[bone.Parent].Name, parent < 0 ? "" : fbx.Bones[parent].Name);
        }
        // Blender adds an unweighted end joint below every bone without children unless told not to.
        Assert.All(fbx.Bones.Where(b => gltf.FindBone(b.Name).IsNone), b => Assert.EndsWith("_end", b.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void Leaf_bones_are_only_added_by_the_default_export()
    {
        Assert.Equal(56, FbxImporter.Load(FixturePath("MaleBaseMesh.zup-scale-all.fbx")).Skeleton.Value.Count);
        var withLeaves = FbxImporter.Load(FixturePath("MaleBaseMesh.blender-defaults.fbx")).Skeleton.Value;
        var leaves = withLeaves.Bones.Where(b => b.Name.EndsWith("_end", StringComparison.Ordinal)).ToList();
        Assert.Equal(withLeaves.Count - 56, leaves.Count);
        Assert.All(leaves, b => Assert.Equal(b.Name[..^"_end".Length], withLeaves.Bones[b.Parent].Name));
    }

    [Fact]
    public void A_file_saved_posed_imports_as_blender_shows_it()
    {
        var mesh = FbxImporter.Load(FixturePath("MaleBaseMesh.posed-in-moved-rig.fbx"));
        var skeleton = mesh.Skeleton.Value;

        // Printed by export_fbx.py from Blender's evaluated mesh and pose, converted to Y up.
        AssertNear(new Vector3(0.39812f, -0.69767f, -0.81142f), mesh.Geometry.Bounds.Min);
        AssertNear(new Vector3(0.67612f, 1.24692f, 0.58360f), mesh.Geometry.Bounds.Max);
        AssertNear(new Vector3(0.52692f, 0.21916f, 0f), skeleton.WorldRest[skeleton.FindBone("spine").Value].Translation);
        AssertNear(new Vector3(0.50362f, 0.60836f, -0.37240f), skeleton.WorldRest[skeleton.FindBone("forearm.L").Value].Translation);
        AssertNear(new Vector3(0.53732f, 0.63375f, -0.60858f), skeleton.WorldRest[skeleton.FindBone("hand.L").Value].Translation);
    }

    [Theory]
    [MemberData(nameof(Exports))]
    public void Fbx_skin_weights_pull_on_the_same_parts_of_the_body(string file)
    {
        var gltf = Original();
        var fbx = FbxImporter.Load(FixturePath(file));
        var gltfCenters = WeightedCenters(gltf);
        var fbxCenters = WeightedCenters(fbx);

        Assert.All(fbx.Geometry.Skin.Value.ToArray(), w =>
        {
            var total = w.TotalWeight;
            Assert.True(MathF.Abs(total - 1f) < 1e-4f, $"Weights add up to {total}.");
        });
        foreach (var (name, center) in gltfCenters)
        {
            Assert.True(fbxCenters.TryGetValue(name, out var other), $"{name} weights no vertices.");
            Assert.True(Vector3.Distance(center, other) < 5e-3f, $"{name}: expected weights around {center}, got {other}.");
        }
    }

    [Fact]
    public void Mesh_importer_picks_the_reader_by_extension()
    {
        Assert.Equal(56, MeshImporter.Load(FixturePath("MaleBaseMesh.zup-scale-all.fbx")).Skeleton.Value.Count);
        Assert.Equal(56, MeshImporter.Load(FixturePath("MaleBaseMesh.glb")).Skeleton.Value.Count);
        Assert.True(MeshImporter.IsSupported("Hero.FBX"));
        Assert.Throws<NotSupportedException>(() => MeshImporter.Load(_temp.Combine("Hero.obj")));
    }

    [Fact]
    public void Missing_and_broken_files_are_reported()
    {
        Assert.Throws<FileNotFoundException>(() => FbxImporter.Load(_temp.Combine("Missing.fbx")));
        var broken = _temp.Combine("Broken.fbx");
        File.WriteAllText(broken, "This is not an FBX file.");
        Assert.Throws<InvalidDataException>(() => FbxImporter.Load(broken));
    }

    private static LoadedMesh Original() => GltfImporter.Load(FixturePath("MaleBaseMesh.glb"));

    internal static string FixturePath(string file) => Path.Combine(AppContext.BaseDirectory, "Fixtures", file);

    /// <summary>Weighted average position of the vertices each bone moves, by bone name.</summary>
    private static Dictionary<string, Vector3> WeightedCenters(LoadedMesh mesh)
    {
        var skeleton = mesh.Skeleton.Value;
        var sums = new Vector3[skeleton.Count];
        var totals = new float[skeleton.Count];
        var positions = mesh.Geometry.Positions.Span;
        var skin = mesh.Geometry.Skin.Value.Span;
        for (var i = 0; i < positions.Length; i++)
        {
            foreach (var (bone, weight) in skin[i].Influences())
            {
                sums[bone] += positions[i] * weight;
                totals[bone] += weight;
            }
        }
        return Enumerable.Range(0, skeleton.Count)
            .Where(b => totals[b] > 0f)
            .ToDictionary(b => skeleton.Bones[b].Name, b => sums[b] / totals[b]);
    }

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < Tolerance, $"Expected {expected}, got {actual}.");
}
