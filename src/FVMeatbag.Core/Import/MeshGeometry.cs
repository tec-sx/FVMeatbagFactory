using System.Numerics;

namespace FVMeatbag.Core.Import;

/// <summary>Axis-aligned bounds.</summary>
public readonly record struct BoundingBox(Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;

    /// <summary>Radius of the sphere around <see cref="Center"/> that encloses the box.</summary>
    public float Radius => Size.Length() * 0.5f;

    public static BoundingBox FromPoints(ReadOnlySpan<Vector3> points)
    {
        if (points.IsEmpty)
        {
            return default;
        }
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return new BoundingBox(min, max);
    }
}

/// <summary>
/// An immutable indexed triangle mesh in the internal convention (glTF: right-handed, +Y up,
/// meters). One position and one normal per vertex, three indices per triangle.
/// </summary>
/// <remarks>
/// Treat it as a snapshot: edits produce a new instance, so graph change detection and GPU
/// caches can compare references.
/// </remarks>
public sealed class MeshGeometry
{
    private readonly Vector3[] _positions;
    private readonly Vector3[] _normals;
    private readonly uint[] _indices;

    public MeshGeometry(Vector3[] positions, Vector3[] normals, uint[] indices)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(indices);
        if (normals.Length != positions.Length)
        {
            throw new ArgumentException("Every vertex needs exactly one normal.", nameof(normals));
        }
        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException("Index count must be a multiple of three.", nameof(indices));
        }
        foreach (var index in indices)
        {
            if (index >= positions.Length)
            {
                throw new ArgumentException($"Index {index} is out of range for {positions.Length} vertices.", nameof(indices));
            }
        }
        _positions = positions;
        _normals = normals;
        _indices = indices;
        Bounds = BoundingBox.FromPoints(positions);
    }

    public ReadOnlyMemory<Vector3> Positions => _positions;
    public ReadOnlyMemory<Vector3> Normals => _normals;
    public ReadOnlyMemory<uint> Indices => _indices;
    public int VertexCount => _positions.Length;
    public int TriangleCount => _indices.Length / 3;
    public BoundingBox Bounds { get; }

    /// <summary>Area-weighted vertex normals, for sources that do not provide any.</summary>
    public static Vector3[] ComputeNormals(ReadOnlySpan<Vector3> positions, ReadOnlySpan<uint> indices)
    {
        var normals = new Vector3[positions.Length];
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var (a, b, c) = (indices[i], indices[i + 1], indices[i + 2]);
            var faceNormal = Vector3.Cross(positions[(int)b] - positions[(int)a], positions[(int)c] - positions[(int)a]);
            normals[a] += faceNormal;
            normals[b] += faceNormal;
            normals[c] += faceNormal;
        }
        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i] == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(normals[i]);
        }
        return normals;
    }
}
