using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Muscles;
using FVMeatbag.Graph;

namespace FVMeatbag.Scene.Operations;

/// <summary>A muscle with its bones looked up in the skeleton and its rest mesh built.</summary>
/// <param name="Index">Position in the <see cref="MuscleSet"/>.</param>
/// <param name="RestLength">Origin to insertion at rest, in model units.</param>
/// <param name="Local">The strand's vertices in its own space (<see cref="MuscleShape.LocalVertices"/>), Fit to Skin applied.</param>
/// <param name="Rest">The strand's space to model space at rest.</param>
public readonly record struct ResolvedMuscle(
    int Index,
    Muscle Muscle,
    int OriginBone,
    int InsertionBone,
    float RestLength,
    ReadOnlyMemory<Vector3> Local,
    Matrix4x4 Rest,
    ReadOnlyMemory<ResolvedHook> Hooks);

/// <summary>A hook of a muscle: a bone that also bends the vertices near its point, like Blender's Hook modifier.</summary>
/// <param name="InverseRest">Inverse of the bone's rest transform, so the pose gives how far the bone moved.</param>
/// <param name="Weights">How much each vertex follows the bone: 1 at the hook point, fading smoothly to 0 at 0.6 rest lengths.</param>
public readonly record struct ResolvedHook(int Bone, Matrix4x4 InverseRest, ReadOnlyMemory<float> Weights);

/// <summary>Where a muscle is in the current pose, model space.</summary>
/// <param name="Facing">Unit direction the thickness axis points, at right angles to the muscle.</param>
/// <param name="Stretch">Posed length over rest length: below 1 when the muscle shortens.</param>
/// <param name="CrossScale">How much wider the belly is than at rest; see <see cref="Muscle.CrossScale"/>.</param>
/// <param name="Transform">The strand's space to model space: stretched along its length, widened across.</param>
/// <param name="Hooks">Per hook, how it moves the strand's vertices, in the strand's own space.</param>
public readonly record struct MuscleFrame(
    Vector3 Origin,
    Vector3 Insertion,
    Vector3 Facing,
    float Stretch,
    float CrossScale,
    Matrix4x4 Transform,
    ReadOnlyMemory<Matrix4x4> Hooks)
{
    public float Length => Vector3.Distance(Origin, Insertion);
}

/// <summary>A muscle's preview mesh for one pose, model space.</summary>
/// <param name="Id">Stable for the muscle while it stays at the same position in its set, so renderers can reuse uploads.</param>
/// <param name="Color">The muscle's display color, sRGB.</param>
public readonly record struct MuscleMesh(
    Guid Id,
    int Index,
    ReadOnlyMemory<Vector3> Positions,
    ReadOnlyMemory<Vector3> Normals,
    ReadOnlyMemory<uint> Indices,
    Vector3 Color,
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
            var bones = new[] { muscle.Origin, muscle.Insertion }.Concat(muscle.Hooks).Select(a => (a.Bone, Index: skeleton.FindBone(a.Bone))).ToList();
            if (bones.FirstOrNone(b => b.Index.IsNone).TryGetValue(out var missing))
            {
                problems.Add($"{muscle.Name}: the skeleton lacks {missing.Bone}.");
                continue;
            }
            resolved.Add(Resolve(i, muscle, bones[0].Index.Value, bones[1].Index.Value, [.. bones.Skip(2).Select(b => b.Index.Value)], skeleton));
        }
        output.Muscles = resolved.ToArray();
        output.Problems = problems;
    }

    public static ResolvedMuscle Resolve(int index, Muscle muscle, int originBone, int insertionBone, IReadOnlyList<int> hookBones, Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(muscle);
        ArgumentNullException.ThrowIfNull(hookBones);
        ArgumentNullException.ThrowIfNull(skeleton);
        var rest = skeleton.WorldRest;
        var origin = Vector3.Transform(muscle.Origin.Local, rest[originBone]);
        var insertion = Vector3.Transform(muscle.Insertion.Local, rest[insertionBone]);
        var length = Vector3.Distance(origin, insertion);
        var local = MuscleShape.LocalVertices(muscle, length);
        var restTransform = MuscleShapeOperation.StrandTransform(origin, insertion, Vector3.TransformNormal(muscle.Facing, rest[originBone]), 1f, 1f);
        var falloff = MathF.Max(length * HookFalloff, 1e-3f);
        var hooks = new ResolvedHook[hookBones.Count];
        for (var h = 0; h < hooks.Length; h++)
        {
            var bone = hookBones[h];
            var center = Vector3.Transform(muscle.Hooks[h].Local, rest[bone]);
            var weights = new float[local.Length];
            for (var v = 0; v < local.Length; v++)
            {
                var t = 1f - (Vector3.Distance(Vector3.Transform(local[v], restTransform), center) / falloff);
                weights[v] = t <= 0f ? 0f : t * t * (3f - (2f * t));
            }
            hooks[h] = new ResolvedHook(bone, Matrix4x4.Invert(rest[bone], out var inverse) ? inverse : Matrix4x4.Identity, weights);
        }
        return new ResolvedMuscle(index, muscle, originBone, insertionBone, length, local, restTransform, hooks);
    }

    /// <summary>A hook's reach, relative to the strand's rest length.</summary>
    public const float HookFalloff = 0.6f;
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
        var length = Vector3.Distance(origin, insertion);
        var stretch = resolved.RestLength > 1e-6f ? length / resolved.RestLength : 1f;
        var cross = muscle.CrossScale(stretch);
        var transform = MuscleShapeOperation.StrandTransform(origin, insertion, Vector3.TransformNormal(muscle.Facing, originWorld), stretch, cross);
        var hookSpan = resolved.Hooks.Span;
        var hooks = new Matrix4x4[hookSpan.Length];
        if (hooks.Length > 0 && Matrix4x4.Invert(transform, out var toStrand))
        {
            for (var h = 0; h < hooks.Length; h++)
            {
                var hook = hookSpan[h];
                // Strand space at rest → model → how the hook bone moved → strand space now.
                hooks[h] = hook.Bone < bones.Length
                    ? resolved.Rest * hook.InverseRest * bones[hook.Bone] * toStrand
                    : Matrix4x4.Identity;
            }
        }
        var facing = new Vector3(transform.M31, transform.M32, transform.M33);
        return new MuscleFrame(origin, insertion, Vector3.Normalize(facing), stretch, cross, transform, hooks);
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
            var (positions, normals) = Build(muscles[i], frames[i]);
            var index = muscles[i].Index;
            meshes[i] = new MuscleMesh(MeshId(input.Owner, index), index, positions, normals, MuscleShape.Triangles, muscles[i].Muscle.Color, input.Selected == index);
        }
        output.Meshes = meshes;
    }

    /// <summary>
    /// The strand's space to model space: Y along the strand scaled by <paramref name="stretch"/>,
    /// X and Z across it scaled by <paramref name="cross"/>, Z towards <paramref name="facing"/>.
    /// </summary>
    public static Matrix4x4 StrandTransform(Vector3 origin, Vector3 insertion, Vector3 facing, float stretch, float cross)
    {
        var (x, y, z) = MuscleShape.Axes(origin, insertion, facing);
        x *= cross;
        y *= stretch;
        z *= cross;
        return new Matrix4x4(
            x.X, x.Y, x.Z, 0f,
            y.X, y.Y, y.Z, 0f,
            z.X, z.Y, z.Z, 0f,
            origin.X, origin.Y, origin.Z, 1f);
    }

    /// <summary>The posed vertices and normals of a strand: hooks bend it in its own space, then it is placed.</summary>
    public static (Vector3[] Positions, Vector3[] Normals) Build(in ResolvedMuscle resolved, in MuscleFrame frame)
    {
        var local = resolved.Local.Span;
        var hooks = resolved.Hooks.Span;
        var hookMatrices = frame.Hooks.Span;
        var positions = new Vector3[local.Length];
        for (var v = 0; v < local.Length; v++)
        {
            var point = local[v];
            for (var h = 0; h < hooks.Length && h < hookMatrices.Length; h++)
            {
                var weight = hooks[h].Weights.Span[v];
                if (weight > 0f)
                {
                    point = Vector3.Lerp(point, Vector3.Transform(point, hookMatrices[h]), weight);
                }
            }
            positions[v] = Vector3.Transform(point, frame.Transform);
        }
        return (positions, Normals(positions, MuscleShape.Triangles));
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

    private static Guid MeshId(Guid owner, int index)
    {
        Span<byte> bytes = stackalloc byte[16];
        owner.TryWriteBytes(bytes);
        BitConverter.TryWriteBytes(bytes[12..], BitConverter.ToInt32(bytes[12..]) ^ (index + 1));
        return new Guid(bytes);
    }
}
