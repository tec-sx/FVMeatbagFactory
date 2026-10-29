using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Graph;

namespace FVMeatbag.Core.Muscles;

/// <summary>
/// The skin at rest, for placing muscles under it: rays to the surface and the distance to it. A
/// bounding volume hierarchy over the triangles keeps both fast on meshes of any size.
/// </summary>
public sealed class SkinSurface
{
    private const int LeafSize = 4;

    private readonly Vector3[] _positions;
    private readonly int[] _triangles; // three vertex indices per triangle, reordered by the tree
    private readonly List<Node> _nodes = [];

    public SkinSurface(ReadOnlySpan<Vector3> positions, ReadOnlySpan<uint> indices)
    {
        _positions = positions.ToArray();
        var count = indices.Length / 3;
        _triangles = new int[count * 3];
        for (var i = 0; i < _triangles.Length; i++)
        {
            _triangles[i] = (int)indices[i];
        }
        var order = Enumerable.Range(0, count).ToArray();
        var centers = new Vector3[count];
        for (var t = 0; t < count; t++)
        {
            centers[t] = (Corner(t, 0) + Corner(t, 1) + Corner(t, 2)) / 3f;
        }
        if (count > 0)
        {
            _nodes.Add(default);
            Build(order, centers, 0, count, 0);
        }
        var sorted = new int[_triangles.Length];
        for (var i = 0; i < count; i++)
        {
            Array.Copy(_triangles, order[i] * 3, sorted, i * 3, 3);
        }
        sorted.CopyTo(_triangles, 0);
    }

    public static SkinSurface From(MeshGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return new SkinSurface(geometry.Positions.Span, geometry.Indices.Span);
    }

    public int TriangleCount => _triangles.Length / 3;

    /// <summary>Distance along <paramref name="direction"/> (unit length) to the first triangle hit, either side facing; None when the ray leaves the mesh.</summary>
    public Maybe<float> RayCast(Vector3 origin, Vector3 direction)
    {
        if (_nodes.Count == 0 || direction.LengthSquared() < 1e-20f)
        {
            return Maybe<float>.None;
        }
        var inverse = new Vector3(1f / direction.X, 1f / direction.Y, 1f / direction.Z);
        var best = float.PositiveInfinity;
        Span<int> stack = stackalloc int[64];
        var top = 0;
        stack[top++] = 0;
        while (top > 0)
        {
            var node = _nodes[stack[--top]];
            if (!HitsBox(node.Min, node.Max, origin, inverse, best))
            {
                continue;
            }
            if (node.Count > 0)
            {
                for (var t = node.First; t < node.First + node.Count; t++)
                {
                    if (IntersectTriangle(origin, direction, Corner(t, 0), Corner(t, 1), Corner(t, 2), out var distance) && distance < best)
                    {
                        best = distance;
                    }
                }
            }
            else if (top + 2 <= stack.Length)
            {
                stack[top++] = node.Left;
                stack[top++] = node.Left + 1;
            }
        }
        return float.IsPositiveInfinity(best) ? Maybe<float>.None : best;
    }

    /// <summary>The point where a ray first meets the skin, or None.</summary>
    public Maybe<Vector3> RayHit(Vector3 origin, Vector3 direction)
    {
        if (direction.LengthSquared() < 1e-20f)
        {
            return Maybe<Vector3>.None;
        }
        var unit = Vector3.Normalize(direction);
        return RayCast(origin, unit).Map(d => origin + (unit * d));
    }

    /// <summary>Distance from <paramref name="point"/> to the nearest point of the skin; None for an empty mesh.</summary>
    public Maybe<float> Distance(Vector3 point)
    {
        if (_nodes.Count == 0)
        {
            return Maybe<float>.None;
        }
        var best = float.PositiveInfinity;
        Span<int> stack = stackalloc int[64];
        var top = 0;
        stack[top++] = 0;
        while (top > 0)
        {
            var node = _nodes[stack[--top]];
            if (BoxDistanceSquared(node.Min, node.Max, point) >= best)
            {
                continue;
            }
            if (node.Count > 0)
            {
                for (var t = node.First; t < node.First + node.Count; t++)
                {
                    best = MathF.Min(best, Vector3.DistanceSquared(point, ClosestOnTriangle(point, Corner(t, 0), Corner(t, 1), Corner(t, 2))));
                }
            }
            else if (top + 2 <= stack.Length)
            {
                // Nearer child last, so it is searched first.
                var a = _nodes[node.Left];
                var b = _nodes[node.Left + 1];
                var nearFirst = BoxDistanceSquared(a.Min, a.Max, point) <= BoxDistanceSquared(b.Min, b.Max, point);
                stack[top++] = nearFirst ? node.Left + 1 : node.Left;
                stack[top++] = nearFirst ? node.Left : node.Left + 1;
            }
        }
        return MathF.Sqrt(best);
    }

    private Vector3 Corner(int triangle, int corner) => _positions[_triangles[(triangle * 3) + corner]];

    private void Build(int[] order, Vector3[] centers, int first, int count, int slot)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        var centerMin = min;
        var centerMax = max;
        for (var i = first; i < first + count; i++)
        {
            var t = order[i];
            foreach (var corner in (ReadOnlySpan<Vector3>)[Corner(t, 0), Corner(t, 1), Corner(t, 2)])
            {
                min = Vector3.Min(min, corner);
                max = Vector3.Max(max, corner);
            }
            centerMin = Vector3.Min(centerMin, centers[t]);
            centerMax = Vector3.Max(centerMax, centers[t]);
        }
        var extent = centerMax - centerMin;
        if (count <= LeafSize || extent.LengthSquared() < 1e-20f)
        {
            _nodes[slot] = new Node(min, max, first, count, 0);
            return;
        }
        var axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        Array.Sort(order, first, count, Comparer<int>.Create((a, b) => Component(centers[a], axis).CompareTo(Component(centers[b], axis))));
        var half = count / 2;
        // Children sit next to each other, so a node only stores the first.
        var left = _nodes.Count;
        _nodes.Add(default);
        _nodes.Add(default);
        _nodes[slot] = new Node(min, max, 0, 0, left);
        Build(order, centers, first, half, left);
        Build(order, centers, first + half, count - half, left + 1);
    }

    private static float Component(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private static bool HitsBox(Vector3 min, Vector3 max, Vector3 origin, Vector3 inverse, float limit)
    {
        var t1 = (min - origin) * inverse;
        var t2 = (max - origin) * inverse;
        var near = Vector3.Min(t1, t2);
        var far = Vector3.Max(t1, t2);
        var enter = MathF.Max(MathF.Max(near.X, near.Y), MathF.Max(near.Z, 0f));
        var exit = MathF.Min(MathF.Min(far.X, far.Y), MathF.Min(far.Z, limit));
        return enter <= exit;
    }

    private static float BoxDistanceSquared(Vector3 min, Vector3 max, Vector3 point) =>
        Vector3.DistanceSquared(point, Vector3.Clamp(point, min, max));

    /// <summary>Möller–Trumbore, both faces; hits at the ray's start do not count.</summary>
    private static bool IntersectTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
    {
        distance = 0f;
        var ab = b - a;
        var ac = c - a;
        var p = Vector3.Cross(direction, ac);
        var determinant = Vector3.Dot(ab, p);
        if (MathF.Abs(determinant) < 1e-12f)
        {
            return false;
        }
        var inverse = 1f / determinant;
        var s = origin - a;
        var u = Vector3.Dot(s, p) * inverse;
        if (u < 0f || u > 1f)
        {
            return false;
        }
        var q = Vector3.Cross(s, ab);
        var v = Vector3.Dot(direction, q) * inverse;
        if (v < 0f || u + v > 1f)
        {
            return false;
        }
        distance = Vector3.Dot(ac, q) * inverse;
        return distance > 1e-6f;
    }

    /// <summary>Closest point on a triangle (Ericson, Real-Time Collision Detection 5.1.5).</summary>
    private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
        {
            return a;
        }
        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
        {
            return b;
        }
        var vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            return a + (d1 / (d1 - d3) * ab);
        }
        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6)
        {
            return c;
        }
        var vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            return a + (d2 / (d2 - d6) * ac);
        }
        var va = (d3 * d6) - (d5 * d4);
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
        {
            return b + ((d4 - d3) / ((d4 - d3) + (d5 - d6)) * (c - b));
        }
        var denominator = 1f / (va + vb + vc);
        return a + (ab * (vb * denominator)) + (ac * (vc * denominator));
    }

    /// <param name="Count">Triangles in a leaf; 0 for an inner node, whose children are <see cref="Left"/> and the one after it.</param>
    private readonly record struct Node(Vector3 Min, Vector3 Max, int First, int Count, int Left);
}
