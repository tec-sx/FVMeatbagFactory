using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Graph;

namespace FVMeatbag.Scene.Operations;

public struct BoneLinesInput
{
    public Maybe<Skeleton> Skeleton;
    public Matrix4x4 World;
    public bool Visible;
}

public struct BoneLinesOutput
{
    /// <summary>Line segments as pairs of world-space points; empty when hidden.</summary>
    public ReadOnlyMemory<Vector3> Segments;
}

/// <summary>
/// Turns a skeleton into lines for the viewport: one from each bone to each child, and for bones
/// without children a short tail along the bone's +Y axis (Blender's bone direction).
/// </summary>
public sealed class BoneLinesOperation : Operation<BoneLinesInput, BoneLinesOutput>
{
    public override void Execute(in BoneLinesInput input, ref BoneLinesOutput output, EvaluationContext context)
    {
        if (!input.Visible || !input.Skeleton.TryGetValue(out var skeleton))
        {
            output.Segments = ReadOnlyMemory<Vector3>.Empty;
            return;
        }

        var points = new List<Vector3>(skeleton.Count * 2);
        var world = input.World;
        var rest = skeleton.WorldRest;
        for (var bone = 0; bone < skeleton.Count; bone++)
        {
            var head = rest[bone].Translation;
            var hasChildren = false;
            foreach (var child in skeleton.ChildrenOf(bone))
            {
                hasChildren = true;
                points.Add(Vector3.Transform(head, world));
                points.Add(Vector3.Transform(rest[child].Translation, world));
            }
            var parent = skeleton.Bones[bone].Parent;
            if (!hasChildren && parent >= 0)
            {
                var length = MathF.Max(Vector3.Distance(head, rest[parent].Translation) * 0.5f, 0.01f);
                var direction = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, rest[bone]));
                points.Add(Vector3.Transform(head, world));
                points.Add(Vector3.Transform(head + (direction * length), world));
            }
        }
        output.Segments = points.ToArray();
    }
}
