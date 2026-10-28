using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Muscles;
using FVMeatbag.Graph;

namespace FVMeatbag.Scene.Operations;

/// <summary>A muscle with its bones looked up in the skeleton.</summary>
/// <param name="Index">Position in the <see cref="MuscleSet"/>.</param>
/// <param name="RestLength">Origin to insertion at rest, in model units.</param>
public readonly record struct ResolvedMuscle(int Index, Muscle Muscle, int OriginBone, int InsertionBone, float RestLength);

/// <summary>Where a muscle is in the current pose, model space.</summary>
/// <param name="Facing">Unit direction the outer surface faces, at right angles to the muscle.</param>
/// <param name="Stretch">Posed length over rest length: below 1 when the muscle shortens.</param>
/// <param name="CrossScale">How much wider the belly is than at rest; see <see cref="Muscle.CrossScale"/>.</param>
public readonly record struct MuscleFrame(Vector3 Origin, Vector3 Insertion, Vector3 Facing, float Stretch, float CrossScale)
{
    public float Length => Vector3.Distance(Origin, Insertion);
}

/// <summary>A muscle's preview mesh for one pose, model space.</summary>
/// <param name="Id">Stable for the muscle while it stays at the same position in its set, so renderers can reuse uploads.</param>
public readonly record struct MuscleMesh(
    Guid Id,
    int Index,
    ReadOnlyMemory<Vector3> Positions,
    ReadOnlyMemory<Vector3> Normals,
    ReadOnlyMemory<uint> Indices,
    bool Selected);

public struct MuscleRigInput
{
    public Maybe<MuscleSet> Muscles;

    /// <summary>The set's <see cref="MuscleSet.Version"/>; the set is edited in place.</summary>
    public long MusclesVersion;

    public Maybe<Skeleton> Skeleton;

    /// <summary>The skeleton's <see cref="Skeleton.Version"/>; renames edit it in place.</summary>
    public long SkeletonVersion;
}

public struct MuscleRigOutput
{
    /// <summary>Muscles whose bones exist, in set order.</summary>
    public ReadOnlyMemory<ResolvedMuscle> Muscles;

    /// <summary>One line per muscle left out, saying which bone is missing.</summary>
    public IReadOnlyList<string> Problems;
}

/// <summary>
/// Looks up each muscle's bones and measures its rest length. Runs only when the muscles or the
/// skeleton change, so the per-pose nodes downstream just index arrays.
/// </summary>
public sealed class MuscleRigOperation : Operation<MuscleRigInput, MuscleRigOutput>
{
    public override void Execute(in MuscleRigInput input, ref MuscleRigOutput output, EvaluationContext context)
    {
        if (!input.Muscles.TryGetValue(out var set) || !input.Skeleton.TryGetValue(out var skeleton))
        {
            output.Muscles = ReadOnlyMemory<ResolvedMuscle>.Empty;
            output.Problems = [];
            return;
        }
        var resolved = new List<ResolvedMuscle>(set.Count);
        var problems = new List<string>();
        for (var i = 0; i < set.Count; i++)
        {
            var muscle = set.Muscles[i];
            var origin = skeleton.FindBone(muscle.Origin.Bone);
            var insertion = skeleton.FindBone(muscle.Insertion.Bone);
            if (!origin.TryGetValue(out var o) || !insertion.TryGetValue(out var n))
            {
                problems.Add($"{muscle.Name}: the skeleton lacks {(origin.IsNone ? muscle.Origin.Bone : muscle.Insertion.Bone)}.");
                continue;
            }
            var length = Vector3.Distance(
                Vector3.Transform(muscle.Origin.Local, skeleton.WorldRest[o]),
                Vector3.Transform(muscle.Insertion.Local, skeleton.WorldRest[n]));
            resolved.Add(new ResolvedMuscle(i, muscle, o, n, length));
        }
        output.Muscles = resolved.ToArray();
        output.Problems = problems;
    }
}

public struct MuscleFrameInput
{
    public ReadOnlyMemory<ResolvedMuscle> Muscles;

    /// <summary>Model-space transform of every bone, from the pose.</summary>
    public ReadOnlyMemory<Matrix4x4> BoneWorld;
}

public struct MuscleFrameOutput
{
    /// <summary>One frame per resolved muscle, same order.</summary>
    public ReadOnlyMemory<MuscleFrame> Frames;
}

/// <summary>
/// Places every muscle in the current pose: its ends follow their bones, its length against the
/// rest length gives how much it has shortened, and that gives its bulge. A few vector operations
/// per muscle, so it runs on every pose change.
/// </summary>
public sealed class MuscleFrameOperation : Operation<MuscleFrameInput, MuscleFrameOutput>
{
    public override void Execute(in MuscleFrameInput input, ref MuscleFrameOutput output, EvaluationContext context)
    {
        var muscles = input.Muscles.Span;
        var bones = input.BoneWorld.Span;
        var frames = new MuscleFrame[muscles.Length];
        for (var i = 0; i < muscles.Length; i++)
        {
            frames[i] = Place(muscles[i], bones);
        }
        output.Frames = frames;
    }

    public static MuscleFrame Place(in ResolvedMuscle resolved, ReadOnlySpan<Matrix4x4> bones)
    {
        if (resolved.OriginBone >= bones.Length || resolved.InsertionBone >= bones.Length)
        {
            return default;
        }
        var muscle = resolved.Muscle;
        var originWorld = bones[resolved.OriginBone];
        var origin = Vector3.Transform(muscle.Origin.Local, originWorld);
        var insertion = Vector3.Transform(muscle.Insertion.Local, bones[resolved.InsertionBone]);
        var axis = insertion - origin;
        var length = axis.Length();
        var direction = length > 1e-6f ? axis / length : Vector3.UnitY;
        var facing = Vector3.TransformNormal(muscle.Facing, originWorld);
        facing -= Vector3.Dot(facing, direction) * direction;
        if (facing.LengthSquared() < 1e-10f)
        {
            // Facing along the muscle: any direction at right angles will do.
            facing = Vector3.Cross(direction, MathF.Abs(direction.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
        }
        var stretch = resolved.RestLength > 1e-6f ? length / resolved.RestLength : 1f;
        return new MuscleFrame(origin, insertion, Vector3.Normalize(facing), stretch, muscle.CrossScale(stretch));
    }
}

public struct MuscleShapeInput
{
    public ReadOnlyMemory<ResolvedMuscle> Muscles;
    public ReadOnlyMemory<MuscleFrame> Frames;

    /// <summary>Base for the meshes' ids, so each muscle keeps its id between runs.</summary>
    public Guid Owner;

    /// <summary>False builds nothing: hidden muscles cost no geometry.</summary>
    public bool Visible;

    /// <summary>Index in the muscle set of the selected muscle, or None.</summary>
    public Maybe<int> Selected;
}

public struct MuscleShapeOutput
{
    public ReadOnlyMemory<MuscleMesh> Meshes;

    /// <summary>New on every run, so renderers know to upload again.</summary>
    public long Version;
}

/// <summary>Builds the see-through preview meshes of the muscles in their current pose.</summary>
public sealed class MuscleShapeOperation : Operation<MuscleShapeInput, MuscleShapeOutput>
{
    // Far from asset and skinning versions, which renderers also track.
    private static long s_version = long.MaxValue / 4;

    public override void Execute(in MuscleShapeInput input, ref MuscleShapeOutput output, EvaluationContext context)
    {
        output.Version = Interlocked.Increment(ref s_version);
        var muscles = input.Muscles.Span;
        var frames = input.Frames.Span;
        if (!input.Visible || muscles.Length != frames.Length)
        {
            output.Meshes = ReadOnlyMemory<MuscleMesh>.Empty;
            return;
        }
        var meshes = new MuscleMesh[muscles.Length];
        for (var i = 0; i < muscles.Length; i++)
        {
            var (positions, normals, indices) = MuscleMeshBuilder.Build(muscles[i].Muscle, frames[i]);
            var index = muscles[i].Index;
            meshes[i] = new MuscleMesh(MeshId(input.Owner, index), index, positions, normals, indices, input.Selected == index);
        }
        output.Meshes = meshes;
    }

    private static Guid MeshId(Guid owner, int index)
    {
        Span<byte> bytes = stackalloc byte[16];
        owner.TryWriteBytes(bytes);
        BitConverter.TryWriteBytes(bytes[12..], BitConverter.ToInt32(bytes[12..]) ^ (index + 1));
        return new Guid(bytes);
    }
}

/// <summary>Triangle mesh of one muscle: a tube from origin to insertion with an elliptical section.</summary>
public static class MuscleMeshBuilder
{
    public const int Rings = 24;
    public const int Segments = 16;

    /// <summary>Tendon radius as a fraction of the belly's rest radius.</summary>
    private const float TendonRadius = 0.18f;

    public static (Vector3[] Positions, Vector3[] Normals, uint[] Indices) Build(Muscle muscle, MuscleFrame frame)
    {
        ArgumentNullException.ThrowIfNull(muscle);
        var axis = frame.Insertion - frame.Origin;
        var length = axis.Length();
        var direction = length > 1e-6f ? axis / length : Vector3.UnitY;
        var depthAxis = frame.Facing;
        var widthAxis = Vector3.Normalize(Vector3.Cross(direction, depthAxis));
        var flatness = Math.Clamp(muscle.Flatness, 0.05f, 1f);
        var tendonStart = Math.Clamp(muscle.Tendons.X, 0f, 0.45f);
        var tendonEnd = Math.Clamp(muscle.Tendons.Y, 0f, 0.45f);
        // Thin muscles are even along their length, thick ones bulge in the middle.
        var exponent = 0.8f * MathF.Pow(2f, -Math.Clamp(muscle.Thickness, -1f, 1f));
        var belly = muscle.Radius * frame.CrossScale;
        var tendon = muscle.Radius * TendonRadius;

        var positions = new Vector3[(Rings * Segments) + 2];
        for (var ring = 0; ring < Rings; ring++)
        {
            var t = (ring + 1f) / (Rings + 1f);
            var u = (t - tendonStart) / Math.Max(1f - tendonStart - tendonEnd, 1e-3f);
            var radius = u is > 0f and < 1f ? tendon + ((belly - tendon) * MathF.Pow(MathF.Sin(MathF.PI * u), exponent)) : tendon;
            var center = frame.Origin + (axis * t);
            for (var segment = 0; segment < Segments; segment++)
            {
                var angle = 2f * MathF.PI * segment / Segments;
                positions[(ring * Segments) + segment] = center
                    + (MathF.Cos(angle) * radius * widthAxis)
                    + (MathF.Sin(angle) * radius * flatness * depthAxis);
            }
        }
        var startTip = Rings * Segments;
        var endTip = startTip + 1;
        positions[startTip] = frame.Origin;
        positions[endTip] = frame.Insertion;

        var indices = new List<uint>(((Rings - 1) * Segments * 6) + (Segments * 6));
        for (var ring = 0; ring + 1 < Rings; ring++)
        {
            for (var segment = 0; segment < Segments; segment++)
            {
                var next = (segment + 1) % Segments;
                var a = (uint)((ring * Segments) + segment);
                var b = (uint)((ring * Segments) + next);
                var c = (uint)(((ring + 1) * Segments) + segment);
                var d = (uint)(((ring + 1) * Segments) + next);
                indices.AddRange([a, c, b, b, c, d]);
            }
        }
        var last = (Rings - 1) * Segments;
        for (var segment = 0; segment < Segments; segment++)
        {
            var next = (segment + 1) % Segments;
            indices.AddRange([(uint)startTip, (uint)segment, (uint)next]);
            indices.AddRange([(uint)endTip, (uint)(last + next), (uint)(last + segment)]);
        }
        var triangles = indices.ToArray();
        return (positions, Normals(positions, triangles), triangles);
    }

    /// <summary>Area-weighted vertex normals from the triangles.</summary>
    private static Vector3[] Normals(Vector3[] positions, uint[] indices)
    {
        var normals = new Vector3[positions.Length];
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var (a, b, c) = (indices[i], indices[i + 1], indices[i + 2]);
            var face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += face;
            normals[b] += face;
            normals[c] += face;
        }
        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].LengthSquared() > 1e-20f ? Vector3.Normalize(normals[i]) : Vector3.UnitY;
        }
        return normals;
    }
}
