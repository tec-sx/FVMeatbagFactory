using System.Numerics;
using FVMeatbag.Core.Rig;
using FVMeatbag.Graph;

namespace FVMeatbag.Core.Muscles;

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
/// One muscle strand, as in the Flicker Void tools: a spindle from an origin on one bone to an
/// insertion on another that stretches between them and keeps its volume, so it gets shorter and
/// thicker as the joint closes. A muscle with several attachments (pectoral, deltoid, trapezius) is
/// a <see cref="Group"/> of strands. Sizes are in model units.
/// </summary>
/// <param name="Name">Unique within the mesh: <c>Biceps_l</c>, or <c>Pectoral2_l</c> for a fan's second strand.</param>
/// <param name="Group">The muscle the strand belongs to: <c>Pectoral_l</c>; the strand's own name for a one-strand muscle.</param>
/// <param name="Template">Template muscle it came from (<c>Pectoral</c>) and its strand index, or None for a hand-made one.</param>
/// <param name="Hooks">Extra points the strand bends with, for broad shapes several joints move (gluteus).</param>
/// <param name="Facing">Direction the strand's thickness axis points (towards the skin), in the origin bone's rest space.</param>
/// <param name="Width">Belly radius across the muscle, at right angles to <see cref="Facing"/>.</param>
/// <param name="Thickness">Belly radius along <see cref="Facing"/>.</param>
/// <param name="Belly">Where the strand is widest, 0 at the origin to 1 at the insertion.</param>
/// <param name="Tendon">Radius at the ends, relative to the belly.</param>
/// <param name="Volume">How much it thickens as it shortens: 1 keeps its volume, 0 never bulges.</param>
/// <param name="Offset">Skin thickness kept over the muscle when it pushes the skin.</param>
/// <param name="Fit">Per ring vertex radius scale from Fit to Skin (<see cref="MuscleShape.FitSize"/> values), or None for the plain spindle.</param>
/// <param name="Color">Display color, sRGB.</param>
public sealed record Muscle(
    string Name,
    string Group,
    Maybe<MuscleSource> Template,
    BoneSide Side,
    MuscleAttachment Origin,
    MuscleAttachment Insertion,
    IReadOnlyList<MuscleAttachment> Hooks,
    Vector3 Facing,
    float Width,
    float Thickness,
    float Belly,
    float Tendon,
    float Volume,
    float Offset,
    Maybe<IReadOnlyList<float>> Fit,
    Vector3 Color,
    IReadOnlyList<MuscleDrive> Drives)
{
    public const float DefaultBelly = 0.45f;
    public const float DefaultTendon = 0.15f;

    /// <summary>Limits of <see cref="CrossScale"/>, so extreme poses stay readable.</summary>
    public const float MaxCrossScale = 4f;

    public const float MinCrossScale = 0.25f;

    /// <summary>
    /// How much wider the strand gets at a length ratio of <paramref name="stretch"/> (posed length
    /// over rest length), like Blender's Stretch To with volume XZ: <c>stretch^(-Volume/2)</c>, so
    /// with Volume 1 the section's area times the length stays constant.
    /// </summary>
    public float CrossScale(float stretch)
    {
        if (stretch <= 1e-4f)
        {
            return MaxCrossScale;
        }
        return Math.Clamp(MathF.Pow(stretch, -Volume / 2f), MinCrossScale, MaxCrossScale);
    }

    /// <summary>
    /// How strongly this muscle drives a corrective tagged <paramref name="tag"/>: the largest weight
    /// of the drives naming its role (and its part, when the drive names one), 0 when none does or
    /// the corrective sits on the other side. A center corrective (abdominals) takes both sides.
    /// </summary>
    public float DriveWeight(CorrectiveTag tag)
    {
        if (tag.Side != BoneSide.Center && tag.Side != Side)
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

/// <summary>Which template muscle a strand came from.</summary>
/// <param name="Muscle">Template muscle name without its side (<c>Pectoral</c>).</param>
/// <param name="Strand">Index of the strand within it.</param>
public readonly record struct MuscleSource(string Muscle, int Strand);
