using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Rig;
using FVMeatbag.Graph;

namespace FVMeatbag.Core.Muscles;

/// <summary>
/// The character's own directions in its rest pose, read from the Manny bones: up from pelvis to
/// head, left from the right shoulder to the left one, forward from both. Templates place muscles
/// in these directions and in <see cref="Unit"/>s, so they fit any size, any up axis and any
/// facing, whatever the bones' own axes are.
/// </summary>
public sealed class BodyFrame
{
    private BodyFrame(Skeleton skeleton, Vector3 center, Vector3 up, Vector3 left, float unit)
    {
        Skeleton = skeleton;
        Center = center;
        Up = up;
        Left = left;
        Forward = Vector3.Normalize(Vector3.Cross(left, up));
        Unit = unit;
    }

    public Skeleton Skeleton { get; }

    /// <summary>The pelvis at rest: a point on the mirror plane.</summary>
    public Vector3 Center { get; }

    public Vector3 Up { get; }
    public Vector3 Left { get; }
    public Vector3 Forward { get; }

    /// <summary>Pelvis to head at rest, in model units. Template sizes are fractions of it.</summary>
    public float Unit { get; }

    /// <summary>The frame of <paramref name="skeleton"/>, or None if it lacks pelvis, head or both upper arms.</summary>
    public static Maybe<BodyFrame> From(Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        Maybe<Vector3> At(string bone) => skeleton.FindBone(bone).Map(b => skeleton.WorldRest[b].Translation);
        if (!At("pelvis").TryGetValue(out var pelvis)
            || !At("head").TryGetValue(out var head)
            || !At("upperarm_l").TryGetValue(out var left)
            || !At("upperarm_r").TryGetValue(out var right))
        {
            return Maybe<BodyFrame>.None;
        }
        var spine = head - pelvis;
        var across = left - right;
        if (spine.LengthSquared() < 1e-10f || across.LengthSquared() < 1e-10f)
        {
            return Maybe<BodyFrame>.None;
        }
        var up = Vector3.Normalize(spine);
        var sideways = across - (Vector3.Dot(across, up) * up);
        if (sideways.LengthSquared() < 1e-10f)
        {
            return Maybe<BodyFrame>.None;
        }
        return new BodyFrame(skeleton, pelvis, up, Vector3.Normalize(sideways), spine.Length());
    }

    /// <summary>
    /// A direction given as (forward, out, up) in body terms, where "out" points away from the
    /// center line on <paramref name="side"/> (towards the character's left for left muscles).
    /// </summary>
    public Vector3 Direction(Vector3 forwardOutUp, BoneSide side)
    {
        var outward = side == BoneSide.Right ? -Left : Left;
        if (side == BoneSide.Center)
        {
            outward = Vector3.Zero;
        }
        return (forwardOutUp.X * Forward) + (forwardOutUp.Y * outward) + (forwardOutUp.Z * Up);
    }

    /// <summary>The attachment's model-space position at rest.</summary>
    public Vector3 RestPosition(MuscleAttachment attachment) =>
        Skeleton.FindBone(attachment.Bone).Match(b => Vector3.Transform(attachment.Local, Skeleton.WorldRest[b]), () => Vector3.Zero);

    /// <summary>A rest-pose model-space point expressed in <paramref name="bone"/>'s rest space.</summary>
    public Vector3 ToLocalPoint(int bone, Vector3 point) =>
        Matrix4x4.Invert(Skeleton.WorldRest[bone], out var inverse) ? Vector3.Transform(point, inverse) : point;

    /// <summary>A rest-pose model-space direction expressed in <paramref name="bone"/>'s rest space, unit length.</summary>
    public Vector3 ToLocalDirection(int bone, Vector3 direction)
    {
        var local = Matrix4x4.Invert(Skeleton.WorldRest[bone], out var inverse) ? Vector3.TransformNormal(direction, inverse) : direction;
        return local.LengthSquared() > 1e-12f ? Vector3.Normalize(local) : Vector3.UnitY;
    }

    public Vector3 Reflect(Vector3 point) => point - (2f * Vector3.Dot(point - Center, Left) * Left);

    public Vector3 ReflectDirection(Vector3 direction) => direction - (2f * Vector3.Dot(direction, Left) * Left);

    /// <summary>
    /// The muscle on the other side: names and bones mirrored (<c>_l</c> ↔ <c>_r</c>), its ends
    /// and facing reflected across the center plane. None if the skeleton lacks a mirrored bone.
    /// </summary>
    public Maybe<Muscle> Mirror(Muscle muscle)
    {
        ArgumentNullException.ThrowIfNull(muscle);
        var origin = MirrorAttachment(muscle.Origin);
        var insertion = MirrorAttachment(muscle.Insertion);
        if (!origin.TryGetValue(out var o) || !insertion.TryGetValue(out var i)
            || !Skeleton.FindBone(muscle.Origin.Bone).TryGetValue(out var sourceBone)
            || !Skeleton.FindBone(o.Bone).TryGetValue(out var targetBone))
        {
            return Maybe<Muscle>.None;
        }
        var facing = ReflectDirection(Vector3.TransformNormal(muscle.Facing, Skeleton.WorldRest[sourceBone]));
        return muscle with
        {
            Name = BoneNames.Mirror(muscle.Name),
            Side = muscle.Side switch
            {
                BoneSide.Left => BoneSide.Right,
                BoneSide.Right => BoneSide.Left,
                _ => BoneSide.Center,
            },
            Origin = o,
            Insertion = i,
            Facing = ToLocalDirection(targetBone, facing),
        };
    }

    private Maybe<MuscleAttachment> MirrorAttachment(MuscleAttachment attachment)
    {
        var name = BoneNames.Mirror(attachment.Bone);
        return Skeleton.FindBone(name).Map(bone =>
            new MuscleAttachment(Skeleton.Bones[bone].Name, ToLocalPoint(bone, Reflect(RestPosition(attachment)))));
    }
}
