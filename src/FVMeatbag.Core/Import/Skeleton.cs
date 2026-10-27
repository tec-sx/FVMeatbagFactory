using System.Numerics;
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
/// Mesh vertices are stored already deformed into this rest pose, so the bind pose is the rest
/// pose: skinning with <see cref="InverseBind"/> and the rest world matrices gives the mesh back
/// unchanged. Poses will be expressed as changes to <see cref="Bone.LocalRest"/>.
/// </remarks>
public sealed class Skeleton
{
    private readonly Bone[] _bones;
    private readonly Matrix4x4[] _worldRest;
    private readonly Matrix4x4[] _inverseBind;

    public Skeleton(IEnumerable<Bone> bones)
    {
        ArgumentNullException.ThrowIfNull(bones);
        _bones = [.. bones];
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
/// Up to four bone influences of one vertex. Weights sum to 1, or are all 0 for a vertex that no
/// bone moves.
/// </summary>
public readonly record struct SkinWeights(ushort Bone0, ushort Bone1, ushort Bone2, ushort Bone3, Vector4 Weights)
{
    public static SkinWeights None => default;

    /// <summary>Keeps the four strongest influences and normalizes them.</summary>
    public static SkinWeights FromInfluences(IEnumerable<(int Bone, float Weight)> influences)
    {
        ArgumentNullException.ThrowIfNull(influences);
        var top = influences
            .Where(i => i.Weight > 0f)
            .GroupBy(i => i.Bone)
            .Select(g => (Bone: g.Key, Weight: g.Sum(i => i.Weight)))
            .OrderByDescending(i => i.Weight)
            .Take(4)
            .ToArray();
        var total = top.Sum(i => i.Weight);
        if (total <= 0f)
        {
            return None;
        }
        ushort B(int i) => i < top.Length ? (ushort)top[i].Bone : (ushort)0;
        float W(int i) => i < top.Length ? top[i].Weight / total : 0f;
        return new SkinWeights(B(0), B(1), B(2), B(3), new Vector4(W(0), W(1), W(2), W(3)));
    }

    public IEnumerable<(int Bone, float Weight)> Influences()
    {
        if (Weights.X > 0f)
        {
            yield return (Bone0, Weights.X);
        }
        if (Weights.Y > 0f)
        {
            yield return (Bone1, Weights.Y);
        }
        if (Weights.Z > 0f)
        {
            yield return (Bone2, Weights.Z);
        }
        if (Weights.W > 0f)
        {
            yield return (Bone3, Weights.W);
        }
    }
}
