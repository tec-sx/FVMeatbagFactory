using System.Numerics;

namespace FVMeatbag.Core.Import;

/// <summary>A bone's transform relative to its parent, as translation, rotation and scale.</summary>
/// <remarks>Row-vector convention like the rest of the code: scale, then rotate, then translate.</remarks>
public readonly record struct BoneTransform(Vector3 Translation, Quaternion Rotation, Vector3 Scale)
{
    public static BoneTransform Identity => new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    public Matrix4x4 ToMatrix() =>
        Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Translation);

    /// <summary>
    /// Euler angles in degrees, X then Y then Z (Maya's default rotate order), that make up
    /// <paramref name="rotation"/>: rotating by X, then Y, then Z gives the same result.
    /// </summary>
    public static Vector3 EulerDegrees(Quaternion rotation)
    {
        // Row vectors: M = Rx · Ry · Rz, so M13 = -sin(y), M23 / M33 = tan(x), M12 / M11 = tan(z).
        var m = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation));
        var y = MathF.Asin(Math.Clamp(-m.M13, -1f, 1f));
        float x, z;
        if (MathF.Abs(m.M13) < 0.9999f)
        {
            x = MathF.Atan2(m.M23, m.M33);
            z = MathF.Atan2(m.M12, m.M11);
        }
        else
        {
            // Gimbal lock: only x ± z is defined; put it all in x.
            x = MathF.Atan2(-m.M32, m.M22);
            z = 0f;
        }
        return new Vector3(float.RadiansToDegrees(x), float.RadiansToDegrees(y), float.RadiansToDegrees(z));
    }

    /// <summary>Splits an affine matrix into translation, rotation and scale; shear is dropped.</summary>
    public static BoneTransform FromMatrix(Matrix4x4 matrix) =>
        Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation)
            ? new BoneTransform(translation, Quaternion.Normalize(rotation), scale)
            : new BoneTransform(matrix.Translation, Quaternion.Identity, Vector3.One);
}

/// <summary>
/// A pose of a <see cref="Import.Skeleton"/>: each bone's transform relative to its parent. A new
/// pose is the rest pose. Edits bump <see cref="Version"/> and raise <see cref="Changed"/>, so the
/// scene graph can compare versions instead of data.
/// </summary>
public sealed class SkeletonPose
{
    private readonly BoneTransform[] _rest;
    private readonly BoneTransform[] _local;

    public SkeletonPose(Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        Skeleton = skeleton;
        _rest = [.. skeleton.Bones.Select(b => BoneTransform.FromMatrix(b.LocalRest))];
        _local = [.. _rest];
    }

    public Skeleton Skeleton { get; }

    /// <summary>Each bone's rest transform, relative to its parent.</summary>
    public IReadOnlyList<BoneTransform> Rest => _rest;

    public long Version { get; private set; }

    /// <summary>Raised after every edit.</summary>
    public event EventHandler? Changed;

    /// <summary>The bone's current transform, relative to its parent.</summary>
    public BoneTransform this[int bone] => _local[bone];

    /// <summary>True when the bone differs from its rest transform.</summary>
    public bool IsPosed(int bone) => _local[bone] != _rest[bone];

    /// <summary>True when no bone differs from its rest transform.</summary>
    public bool IsRest => !Enumerable.Range(0, _local.Length).Any(IsPosed);

    public void Set(int bone, BoneTransform transform)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _local.Length);
        if (_local[bone] == transform)
        {
            return;
        }
        _local[bone] = transform;
        OnChanged();
    }

    public void ResetBone(int bone) => Set(bone, _rest[bone]);

    /// <summary>Puts every bone back into its rest transform.</summary>
    public void Reset()
    {
        if (IsRest)
        {
            return;
        }
        _rest.CopyTo(_local, 0);
        OnChanged();
    }

    /// <summary>Model-space transform of every bone. Bones still at rest use the skeleton's exact rest matrices.</summary>
    public Matrix4x4[] ComputeWorld()
    {
        var bones = Skeleton.Bones;
        var world = new Matrix4x4[bones.Count];
        for (var i = 0; i < world.Length; i++)
        {
            var local = IsPosed(i) ? _local[i].ToMatrix() : bones[i].LocalRest;
            world[i] = bones[i].Parent < 0 ? local : local * world[bones[i].Parent]; // row vectors: child, then parent
        }
        return world;
    }

    /// <summary>
    /// Skinning matrix of every bone for <paramref name="world"/> from <see cref="ComputeWorld"/>:
    /// takes a rest-pose vertex to its posed position. Identity for bones at rest.
    /// </summary>
    public Matrix4x4[] ComputeSkinMatrices(Matrix4x4[] world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var skin = new Matrix4x4[world.Length];
        for (var i = 0; i < skin.Length; i++)
        {
            skin[i] = Skeleton.InverseBind[i] * world[i];
        }
        return skin;
    }

    private void OnChanged()
    {
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
