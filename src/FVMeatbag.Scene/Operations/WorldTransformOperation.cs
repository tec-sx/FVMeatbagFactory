using System.Numerics;
using FVMeatbag.Graph;

namespace FVMeatbag.Scene.Operations;

public struct WorldTransformInput
{
    public Transform Local;
    public Matrix4x4 ParentWorld;
}

public struct WorldTransformOutput
{
    public Matrix4x4 World;
}

/// <summary>Combines a component's local transform with its parent's world matrix.</summary>
public sealed class WorldTransformOperation : Operation<WorldTransformInput, WorldTransformOutput>
{
    public override void Execute(in WorldTransformInput input, ref WorldTransformOutput output, EvaluationContext context) =>
        output.World = input.Local.ToMatrix() * input.ParentWorld; // System.Numerics uses row vectors: child * parent
}
