using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FVMeatbag.Graph;

namespace FVMeatbag.Core.Import;

/// <summary>A joint of a <see cref="Skeleton"/>.</summary>
/// <param name="Parent">Index of the parent bone, or -1 for a root. Always lower than this bone's own index.</param>
/// <param name="LocalRest">Rest transform relative to the parent bone (relative to the model for a root).</param>
public sealed record Bone(string Name, int Parent, Matrix4x4 LocalRest);

/// <summary>
/// A bone hierarchy in its rest pose, in the internal convention (glTF: right-handed, +Y up).
/// Bones are ordered so every parent comes before its children.
/// </summary>
/// <remarks>
/// <para>
/// Mesh vertices are stored already deformed into this rest pose, so the bind pose is the rest
/// pose: skinning with <see cref="InverseBind"/> and the rest world matrices gives the mesh back
/// unchanged. A <see cref="SkeletonPose"/> holds a pose as each bone's transform relative to its parent.
/// </para>
/// <para>
/// The hierarchy and rest pose never change. Bone names can (<see cref="Rename"/>), so skeletons
/// can be fitted to a naming contract; renames bump <see cref="Version"/>, and the name each bone
/// was imported with stays available as <see cref="SourceName"/>.
/// </para>
/// </remarks>
public sealed class Skeleton
{
    private readonly Bone[] _bones;
    private readonly string[] _sourceNames;
    private readonly Matrix4x4[] _worldRest;
    private readonly Matrix4x4[] _inverseBind;

    public Skeleton(IEnumerable<Bone> bones)
    {
        ArgumentNullException.ThrowIfNull(bones);
        _bones = [.. bones];
        _sourceNames = [.. _bones.Select(b => b.Name)];
        _worldRest = new Matrix4x4[_bones.Length];
        _inverseBind = new Matrix4x4[_bones.Length];
        for (var i = 0; i < _bones.Length; i++)
        {
            var parent = _bones[i].Parent;
            if (parent >= i || parent < -1)
            {
                throw new ArgumentException($"Bone {_bones[i].Name} must come after its parent.", nameof(bones));
            }
            // Row vectors: local first, then the parent's world.
            _worldRest[i] = parent < 0 ? _bones[i].LocalRest : _bones[i].LocalRest * _worldRest[parent];
            _inverseBind[i] = Matrix4x4.Invert(_worldRest[i], out var inverse) ? inverse : Matrix4x4.Identity;
        }
    }

    public IReadOnlyList<Bone> Bones => _bones;
    public int Count => _bones.Length;

    /// <summary>Bumped by every <see cref="Rename"/>.</summary>
    public long Version { get; private set; }

    /// <summary>Raised after a bone was renamed.</summary>
    public event EventHandler? Renamed;

    /// <summary>The name the bone had when the skeleton was created (imported).</summary>
    public string SourceName(int bone) => _sourceNames[bone];

    /// <summary>
    /// Renames a bone. Names must be non-empty and unique ignoring case, as UE requires; returns
    /// false (and changes nothing) for a name another bone already has.
    /// </summary>
    public bool Rename(int bone, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _bones.Length);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();
        if (_bones[bone].Name == name)
        {
            return true;
        }
        for (var i = 0; i < _bones.Length; i++)
        {
            if (i != bone && string.Equals(_bones[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        _bones[bone] = _bones[bone] with { Name = name };
        Version++;
        Renamed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Model-space rest transform of each bone.</summary>
    public IReadOnlyList<Matrix4x4> WorldRest => _worldRest;

    /// <summary>Inverse of <see cref="WorldRest"/>: takes a rest-pose vertex into a bone's space.</summary>
    public IReadOnlyList<Matrix4x4> InverseBind => _inverseBind;

    public Maybe<int> FindBone(string name)
    {
        var index = Array.FindIndex(_bones, b => b.Name == name);
        return index < 0 ? Maybe<int>.None : index;
    }

    public IEnumerable<int> ChildrenOf(int bone)
    {
        for (var i = bone + 1; i < _bones.Length; i++)
        {
            if (_bones[i].Parent == bone)
            {
                yield return i;
            }
        }
    }
}

/// <summary>
/// Up to <see cref="MaxInfluences"/> bone influences of one vertex, strongest first. Weights sum to
/// 1, or are all 0 for a vertex that no bone moves.
/// </summary>
/// <remarks>
/// Eight matches what UE5 imports by default and leaves room for corrective bones on top of the
/// main skeleton. Unused slots have bone 0 and weight 0.
/// </remarks>
public readonly record struct SkinWeights
{
    public const int MaxInfluences = 8;

    private readonly BoneSlots _bones;
    private readonly Vector4 _weights0;
    private readonly Vector4 _weights1;

    private SkinWeights(BoneSlots bones, Vector4 weights0, Vector4 weights1)
    {
        _bones = bones;
        _weights0 = weights0;
        _weights1 = weights1;
    }

    public static SkinWeights None => default;

    /// <summary>The bone in <paramref name="slot"/> (0 to <see cref="MaxInfluences"/> - 1).</summary>
    public ushort Bone(int slot) => _bones[CheckSlot(slot)];

    /// <summary>The weight in <paramref name="slot"/> (0 to <see cref="MaxInfluences"/> - 1).</summary>
    public float Weight(int slot) => CheckSlot(slot) < 4 ? _weights0[slot] : _weights1[slot - 4];

    /// <summary>Sum of all weights: 1 for a skinned vertex, 0 for one no bone moves.</summary>
    public float TotalWeight => Vector4.Dot(_weights0, Vector4.One) + Vector4.Dot(_weights1, Vector4.One);

    /// <summary>Keeps the <see cref="MaxInfluences"/> strongest influences and normalizes them.</summary>
    public static SkinWeights FromInfluences(IEnumerable<(int Bone, float Weight)> influences)
    {
        ArgumentNullException.ThrowIfNull(influences);
        var top = influences
            .Where(i => i.Weight > 0f)
            .GroupBy(i => i.Bone)
            .Select(g => (Bone: g.Key, Weight: g.Sum(i => i.Weight)))
            .OrderByDescending(i => i.Weight)
            .ThenBy(i => i.Bone)
            .Take(MaxInfluences)
            .ToArray();
        var total = top.Sum(i => i.Weight);
        if (total <= 0f)
        {
            return None;
        }
        var bones = default(BoneSlots);
        Span<float> weights = stackalloc float[MaxInfluences];
        for (var i = 0; i < top.Length; i++)
        {
            bones[i] = checked((ushort)top[i].Bone);
            weights[i] = top[i].Weight / total;
        }
        return new SkinWeights(bones, new Vector4(weights[..4]), new Vector4(weights[4..]));
    }

    /// <summary>The non-zero influences, strongest first.</summary>
    public IEnumerable<(int Bone, float Weight)> Influences()
    {
        for (var slot = 0; slot < MaxInfluences; slot++)
        {
            var weight = Weight(slot);
            if (weight > 0f)
            {
                yield return (Bone(slot), weight);
            }
        }
    }

    public bool Equals(SkinWeights other) =>
        ((ReadOnlySpan<ushort>)_bones).SequenceEqual(other._bones) && _weights0 == other._weights0 && _weights1 == other._weights1;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(MemoryMarshal.AsBytes((ReadOnlySpan<ushort>)_bones));
        hash.Add(_weights0);
        hash.Add(_weights1);
        return hash.ToHashCode();
    }

    private static int CheckSlot(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, MaxInfluences);
        return slot;
    }

    [InlineArray(MaxInfluences)]
    private struct BoneSlots
    {
        private ushort _element;
    }
}
