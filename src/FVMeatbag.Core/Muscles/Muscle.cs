using System.Numerics;
using FVMeatbag.Core.Rig;
using FVMeatbag.Graph;

namespace FVMeatbag.Core.Muscles;

/// <summary>How a muscle's cross-section looks.</summary>
public enum MuscleShape
{
    /// <summary>A spindle, round in section: biceps, hamstrings, calf.</summary>
    Fusiform,

    /// <summary>A sheet, flat in section: pectoralis, latissimus, trapezius, abdominals.</summary>
    Flat,
}

/// <summary>Which way of moving the joint makes the muscle bulge.</summary>
public enum MuscleAction
{
    /// <summary>Bulges as it shortens, like a real muscle contracting. Right for almost every muscle.</summary>
    Flexor,

    /// <summary>Bulges as it lengthens. For a muscle whose straight line runs the wrong side of a joint.</summary>
    Extensor,
}

/// <summary>Where a muscle end sits: a point fixed to a bone, in that bone's rest space.</summary>
/// <param name="Bone">Bone name, matched like UE's FName (ignoring case).</param>
/// <param name="Local">Position relative to the bone's rest transform, in model units.</param>
public readonly record struct MuscleAttachment(string Bone, Vector3 Local);

/// <summary>
/// A link from a muscle to the corrective bones of one role: the muscle drives every corrective
/// tagged with <see cref="Role"/> on its side (and <see cref="Part"/>, when given). Muscles and
/// correctives are many-to-many: a muscle drives several roles, and several muscles can drive one.
/// </summary>
/// <param name="Weight">How strongly the muscle moves the corrective, 0 to 1.</param>
public readonly record struct MuscleDrive(string Role, Maybe<string> Part, float Weight);

/// <summary>
/// One muscle: a stretchy shape between two bones that keeps its volume, so it gets shorter and
/// thicker as the joint between them closes. All sizes are in model units.
/// </summary>
/// <param name="Name">Unique within the mesh, UE style: <c>biceps_l</c>.</param>
/// <param name="Template">Id of the template entry it came from, or None for a hand-made muscle.</param>
/// <param name="Facing">Direction the muscle's outer surface faces, in the origin bone's rest space. Orients flat muscles.</param>
/// <param name="Radius">Half the belly's width at rest.</param>
/// <param name="Flatness">Depth over width of the cross-section: 1 is round, 0.3 a sheet.</param>
/// <param name="Thickness">-1 thin and even to 1 thick and bulging in the middle; 0 is a natural spindle.</param>
/// <param name="Volume">How strongly it keeps its volume: 1 keeps it exactly, 0 makes it a rigid tube.</param>
/// <param name="Tendons">Fractions of the length taken by the origin and insertion tendons.</param>
public sealed record Muscle(
    string Name,
    Maybe<string> Template,
    BoneSide Side,
    MuscleAttachment Origin,
    MuscleAttachment Insertion,
    Vector3 Facing,
    MuscleShape Shape,
    MuscleAction Action,
    float Radius,
    float Flatness,
    float Thickness,
    float Volume,
    Vector2 Tendons,
    IReadOnlyList<MuscleDrive> Drives)
{
    /// <summary>Limits of <see cref="CrossScale"/>, so extreme poses stay readable.</summary>
    public const float MaxCrossScale = 2.5f;

    public const float MinCrossScale = 0.4f;

    /// <summary>
    /// How much wider the belly gets for a length ratio of <paramref name="stretch"/> (posed length
    /// over rest length): <c>stretch^(-Volume/2)</c>, so with Volume 1 the section's area times the
    /// length stays constant. Extensors use the inverse ratio.
    /// </summary>
    public float CrossScale(float stretch)
    {
        if (stretch <= 1e-4f)
        {
            return MaxCrossScale;
        }
        var ratio = Action == MuscleAction.Extensor ? 1f / stretch : stretch;
        return Math.Clamp(MathF.Pow(ratio, -Volume / 2f), MinCrossScale, MaxCrossScale);
    }

    /// <summary>
    /// How strongly this muscle drives a corrective tagged <paramref name="tag"/>: the largest weight
    /// of the drives naming its role (and its part, when the drive names one), 0 when none does or
    /// the corrective sits on the other side.
    /// </summary>
    public float DriveWeight(CorrectiveTag tag)
    {
        if (tag.Side != Side)
        {
            return 0f;
        }
        return Drives
            .Where(d => string.Equals(d.Role, tag.Role, StringComparison.OrdinalIgnoreCase))
            .Where(d => d.Part.IsNone || d.Part == tag.Part)
            .Select(d => d.Weight)
            .DefaultIfEmpty(0f)
            .Max();
    }
}
