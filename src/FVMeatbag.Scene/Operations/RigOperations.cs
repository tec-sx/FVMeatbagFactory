using FVMeatbag.Core.Import;
using FVMeatbag.Core.Rig;
using FVMeatbag.Graph;

namespace FVMeatbag.Scene.Operations;

public struct MannyCheckInput
{
    public Maybe<Skeleton> Skeleton;

    /// <summary>The skeleton's <see cref="Skeleton.Version"/>; renames edit the skeleton in place.</summary>
    public long SkeletonVersion;
}

public struct MannyCheckOutput
{
    /// <summary>The full report, problems included; None without a skeleton.</summary>
    public Maybe<MannyCheck> Check;

    /// <summary>The rig split into core bones and extras; None unless the skeleton conforms.</summary>
    public Maybe<MannyRig> Rig;
}

/// <summary>
/// Checks a skeleton against the UE5 Manny hierarchy. Everything muscle- and corrective-related
/// reads <see cref="MannyCheckOutput.Rig"/>, so a non-conforming skeleton stops those nodes with
/// a clear "not a Manny skeleton" instead of failing on a missing bone later.
/// </summary>
public sealed class MannyCheckOperation : Operation<MannyCheckInput, MannyCheckOutput>
{
    public override void Execute(in MannyCheckInput input, ref MannyCheckOutput output, EvaluationContext context)
    {
        output.Check = input.Skeleton.Map(MannyRig.Check);
        output.Rig = output.Check.Bind(c => c.Rig);
    }
}
