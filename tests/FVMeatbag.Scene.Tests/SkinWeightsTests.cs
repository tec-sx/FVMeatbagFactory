using System.Numerics;
using FVMeatbag.Core.Import;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace FVMeatbag.Scene.Tests;

public sealed class SkinWeightsTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Keeps_the_eight_strongest_influences_normalized_and_strongest_first()
    {
        var influences = Enumerable.Range(0, 10).Select(b => (b, (float)(b + 1))).ToArray();

        var weights = SkinWeights.FromInfluences(influences);

        var kept = weights.Influences().ToArray();
        Assert.Equal(SkinWeights.MaxInfluences, kept.Length);
        Assert.Equal([9, 8, 7, 6, 5, 4, 3, 2], kept.Select(i => i.Bone));
        Assert.Equal(1f, weights.TotalWeight, 1e-5f);
        Assert.Equal(10f / 52f, weights.Weight(0), 1e-5f);
    }

    [Fact]
    public void Equal_influences_give_equal_weights()
    {
        var a = SkinWeights.FromInfluences([(3, 0.5f), (7, 0.25f), (1, 0.25f)]);
        var b = SkinWeights.FromInfluences([(1, 1f), (3, 2f), (7, 1f)]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, SkinWeights.FromInfluences([(3, 1f)]));
        Assert.Equal(0f, SkinWeights.None.TotalWeight);
    }

    [Fact]
    public void Gltf_import_reads_both_joint_sets()
    {
        var mesh = GltfImporter.Load(WriteSixInfluenceTriangle(_temp.Combine("Six.glb")));

        var skin = mesh.Geometry.Skin.Value.Span;
        Assert.All(skin.ToArray(), w =>
        {
            Assert.Equal(6, w.Influences().Count());
            Assert.Equal(1f, w.TotalWeight, 1e-5f);
        });
    }

    [Fact]
    public void Skinning_blends_all_eight_slots()
    {
        var mesh = GltfImporter.Load(WriteSixInfluenceTriangle(_temp.Combine("Six.glb")));
        var skeleton = mesh.Skeleton.Value;
        var pose = new SkeletonPose(skeleton);
        // Move only the weakest influence (bone 5, weight 1/21) by 21 units along X: every vertex moves by one unit.
        var weakest = mesh.Geometry.Skin.Value.Span[0].Influences().Last().Bone;
        var rest = pose.Rest[weakest];
        pose.Set(weakest, rest with { Translation = rest.Translation + new Vector3(21f, 0, 0) });

        var posed = Graph.Graph.CreateRoot();
        var poseNode = posed.AddNode(new Operations.PoseOperation(), initialInput: new Operations.PoseInput { Pose = pose, Version = pose.Version });
        var skinning = posed.AddNode(new Operations.SkinningOperation(), initialInput: new Operations.SkinningInput { Geometry = mesh.Geometry });
        posed.Connect(poseNode.Output(nameof(Operations.PoseOutput.Skin)), skinning.Input(nameof(Operations.SkinningInput.Skin)));
        posed.Connect(poseNode.Output(nameof(Operations.PoseOutput.IsRest)), skinning.Input(nameof(Operations.SkinningInput.IsRest)));

        var before = mesh.Geometry.Positions.Span;
        var after = skinning.OutputValues.Positions.Span;
        for (var i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i].X + 1f, after[i].X, 1e-4f);
        }
    }

    /// <summary>A triangle bound to six sibling bones with weights 6:5:4:3:2:1 (bone 0 strongest).</summary>
    private static string WriteSixInfluenceTriangle(string path)
    {
        var mesh = new MeshBuilder<VertexPositionNormal, VertexEmpty, VertexJoints8>("Triangle");
        var primitive = mesh.UsePrimitive(MaterialBuilder.CreateDefault());
        var joints = new VertexJoints8((0, 6f / 21), (1, 5f / 21), (2, 4f / 21), (3, 3f / 21), (4, 2f / 21), (5, 1f / 21));
        VertexBuilder<VertexPositionNormal, VertexEmpty, VertexJoints8> V(Vector3 p) => new(new VertexPositionNormal(p, Vector3.UnitZ), default, joints);
        primitive.AddTriangle(V(Vector3.Zero), V(Vector3.UnitX), V(Vector3.UnitY));

        var root = new NodeBuilder("Root");
        var bones = Enumerable.Range(0, 6).Select(i => root.CreateNode($"Bone{i}")).ToArray();
        var scene = new SceneBuilder();
        scene.AddSkinnedMesh(mesh, [.. bones.Select(b => (b, Matrix4x4.Identity))]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        scene.ToGltf2().SaveGLB(path);
        return path;
    }
}
