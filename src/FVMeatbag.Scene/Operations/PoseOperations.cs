using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Graph;

namespace FVMeatbag.Scene.Operations;

public struct PoseInput
{
    public Maybe<SkeletonPose> Pose;

    /// <summary>The pose's <see cref="SkeletonPose.Version"/>; the pose object itself is edited in place.</summary>
    public long Version;
}

public struct PoseOutput
{
    /// <summary>Model-space transform of every bone.</summary>
    public ReadOnlyMemory<Matrix4x4> BoneWorld;

    /// <summary>Skinning matrix of every bone: rest-pose vertex to posed vertex.</summary>
    public ReadOnlyMemory<Matrix4x4> Skin;

    /// <summary>True when every bone is at rest, so skinning can be skipped.</summary>
    public bool IsRest;
}

/// <summary>Turns a skeleton pose into model-space bone transforms and skinning matrices.</summary>
public sealed class PoseOperation : Operation<PoseInput, PoseOutput>
{
    public override void Execute(in PoseInput input, ref PoseOutput output, EvaluationContext context)
    {
        if (!input.Pose.TryGetValue(out var pose))
        {
            output = default;
            output.IsRest = true;
            return;
        }
        var world = pose.ComputeWorld();
        output.BoneWorld = world;
        output.Skin = pose.ComputeSkinMatrices(world);
        output.IsRest = pose.IsRest;
    }
}

public struct SkinningInput
{
    public Maybe<MeshGeometry> Geometry;
    public ReadOnlyMemory<Matrix4x4> Skin;
    public bool IsRest;
}

public struct SkinningOutput
{
    /// <summary>Posed vertex positions in model space; the geometry's own arrays at rest.</summary>
    public ReadOnlyMemory<Vector3> Positions;

    public ReadOnlyMemory<Vector3> Normals;

    /// <summary>New on every run, so renderers know to upload again.</summary>
    public long Version;
}

/// <summary>
/// Linear blend skinning on the CPU: each vertex is moved by up to four bones. Enough for base
/// meshes; GPU skinning can replace it without changing the ports.
/// </summary>
public sealed class SkinningOperation : Operation<SkinningInput, SkinningOutput>
{
    // Far away from asset versions, which renderers also see for the same mesh id.
    private static long s_version = long.MaxValue / 2;

    public override void Execute(in SkinningInput input, ref SkinningOutput output, EvaluationContext context)
    {
        output.Version = Interlocked.Increment(ref s_version);
        if (!input.Geometry.TryGetValue(out var geometry))
        {
            output.Positions = ReadOnlyMemory<Vector3>.Empty;
            output.Normals = ReadOnlyMemory<Vector3>.Empty;
            return;
        }
        var skin = input.Skin.Span;
        if (input.IsRest || skin.IsEmpty || !geometry.Skin.TryGetValue(out var weightsMemory))
        {
            output.Positions = geometry.Positions;
            output.Normals = geometry.Normals;
            return;
        }

        var weights = weightsMemory.Span;
        var sourcePositions = geometry.Positions.Span;
        var sourceNormals = geometry.Normals.Span;
        var positions = new Vector3[sourcePositions.Length];
        var normals = new Vector3[sourceNormals.Length];
        for (var v = 0; v < positions.Length; v++)
        {
            var w = v < weights.Length ? weights[v] : SkinWeights.None;
            var total = w.Weights.X + w.Weights.Y + w.Weights.Z + w.Weights.W;
            if (total <= 0f)
            {
                positions[v] = sourcePositions[v];
                if (v < normals.Length)
                {
                    normals[v] = sourceNormals[v];
                }
                continue;
            }
            var blended = (Bone(skin, w.Bone0) * w.Weights.X) + (Bone(skin, w.Bone1) * w.Weights.Y)
                + (Bone(skin, w.Bone2) * w.Weights.Z) + (Bone(skin, w.Bone3) * w.Weights.W);
            positions[v] = Vector3.Transform(sourcePositions[v], blended);
            if (v < normals.Length)
            {
                var normal = Vector3.TransformNormal(sourceNormals[v], blended);
                normals[v] = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : sourceNormals[v];
            }
        }
        output.Positions = positions;
        output.Normals = normals;
    }

    private static Matrix4x4 Bone(ReadOnlySpan<Matrix4x4> skin, ushort bone) => bone < skin.Length ? skin[bone] : Matrix4x4.Identity;
}
