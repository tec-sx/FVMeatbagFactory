using System.Numerics;
using FVMeatbag.Graph;

namespace FVMeatbag.Core.Muscles;

/// <summary>
/// The strand's mesh, as in the Flicker Void tools: rings around +Y from 0 to the rest length,
/// closed by a tip at each end, with thin tendons at the ends and the belly between. Local X is the
/// width axis and local Z the thickness axis (<see cref="Muscle.Facing"/>).
/// </summary>
public static class MuscleShape
{
    /// <summary>Rings including the two tips.</summary>
    public const int Rings = 12;

    public const int Segments = 12;

    /// <summary>Rings between the tips.</summary>
    public const int InnerRings = Rings - 2;

    /// <summary>Values in <see cref="Muscle.Fit"/>: one per vertex of the inner rings.</summary>
    public const int FitSize = InnerRings * Segments;

    public const int VertexCount = FitSize + 2;

    /// <summary>Relative radius (0 to 1) at <paramref name="t"/> along the strand: widest at <paramref name="belly"/>, <paramref name="tendon"/> at the ends.</summary>
    public static float RadiusProfile(float t, float belly, float tendon)
    {
        t = Math.Clamp(t, 0f, 1f);
        belly = Math.Clamp(belly, 0.05f, 0.95f);
        var warped = MathF.Pow(t, MathF.Log(0.5f) / MathF.Log(belly)); // moves the belly to 0.5
        var bump = MathF.Pow(MathF.Max(MathF.Sin(MathF.PI * warped), 0f), 0.75f);
        return tendon + ((1f - tendon) * bump);
    }

    /// <summary>Fraction along the strand of inner ring <paramref name="ring"/>.</summary>
    public static float RingT(int ring) => (ring + 1f) / (Rings - 1f);

    /// <summary>
    /// The strand's vertices in its local space at rest: the origin tip (index 0), the inner rings
    /// ring by ring, then the insertion tip.
    /// </summary>
    public static Vector3[] LocalVertices(Muscle muscle, float length, bool fitted = true)
    {
        ArgumentNullException.ThrowIfNull(muscle);
        var fit = fitted ? muscle.Fit.Where(f => f.Count == FitSize) : Maybe<IReadOnlyList<float>>.None;
        var vertices = new Vector3[VertexCount];
        for (var ring = 0; ring < InnerRings; ring++)
        {
            var t = RingT(ring);
            var radius = RadiusProfile(t, muscle.Belly, muscle.Tendon);
            for (var segment = 0; segment < Segments; segment++)
            {
                var angle = 2f * MathF.PI * segment / Segments;
                var r = radius * fit.Match(f => f[(ring * Segments) + segment], () => 1f);
                vertices[1 + (ring * Segments) + segment] = new Vector3(MathF.Cos(angle) * muscle.Width * r, t * length, MathF.Sin(angle) * muscle.Thickness * r);
            }
        }
        vertices[^1] = new Vector3(0f, length, 0f);
        return vertices;
    }

    /// <summary>Triangles of the strand mesh, wound so normals point outwards.</summary>
    public static uint[] Triangles { get; } = BuildTriangles();

    /// <summary>
    /// The strand's frame from its ends: Y from origin to insertion, Z the facing made square to Y,
    /// X = Y × Z. A facing along the muscle falls back to any square direction.
    /// </summary>
    public static (Vector3 X, Vector3 Y, Vector3 Z) Axes(Vector3 origin, Vector3 insertion, Vector3 facing)
    {
        var axis = insertion - origin;
        var y = axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : Vector3.UnitY;
        var z = facing - (Vector3.Dot(facing, y) * y);
        if (z.LengthSquared() < 1e-10f)
        {
            z = Vector3.Cross(y, MathF.Abs(y.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
        }
        z = Vector3.Normalize(z);
        return (Vector3.Cross(y, z), y, z);
    }

    /// <summary>
    /// Fit to Skin: gives every ring vertex its own radius, <paramref name="room"/> of the distance
    /// to the skin straight out from the strand's line at rest, so it flattens under flat skin and
    /// fills round limbs. Growth is capped so a strand doesn't fill the body cavity behind it; each
    /// ring is smoothed so one stray ray makes no spike.
    /// </summary>
    /// <param name="origin">Rest position of the origin, model space.</param>
    /// <param name="facing">Rest facing, model space.</param>
    public static IReadOnlyList<float> FitToSkin(
        Muscle muscle, Vector3 origin, Vector3 insertion, Vector3 facing, SkinSurface skin,
        float room = 0.85f, float grow = 1.6f, float shrink = 0.2f)
    {
        ArgumentNullException.ThrowIfNull(muscle);
        ArgumentNullException.ThrowIfNull(skin);
        var length = Vector3.Distance(origin, insertion);
        var (x, y, z) = Axes(origin, insertion, facing);
        var local = LocalVertices(muscle, length, fitted: false);
        var raw = new float[FitSize];
        for (var i = 0; i < FitSize; i++)
        {
            raw[i] = 1f;
            var vertex = local[i + 1];
            var radial = (vertex.X * x) + (vertex.Z * z);
            var current = radial.Length();
            if (current < 1e-9f)
            {
                continue;
            }
            var center = origin + (vertex.Y * y);
            if (skin.RayCast(center, radial / current).TryGetValue(out var distance))
            {
                var taper = RadiusProfile(vertex.Y / Math.Max(length, 1e-6f), muscle.Belly, muscle.Tendon);
                raw[i] = distance * room * taper / current; // the belly fills the room, the tendons keep their taper
            }
        }
        var fit = new float[FitSize];
        for (var ring = 0; ring < InnerRings; ring++)
        {
            for (var segment = 0; segment < Segments; segment++)
            {
                float At(int s) => raw[(ring * Segments) + ((s + Segments) % Segments)];
                var average = (At(segment) + At(segment - 1) + At(segment + 1)) / 3f;
                fit[(ring * Segments) + segment] = Math.Clamp(MathF.Min(At(segment), average), shrink, grow);
            }
        }
        return fit;
    }

    private static uint[] BuildTriangles()
    {
        var triangles = new List<uint>();
        const uint last = VertexCount - 1;
        var top = (uint)(1 + ((InnerRings - 1) * Segments));
        for (var s = 0u; s < Segments; s++)
        {
            var n = (s + 1) % Segments;
            triangles.AddRange([0u, 1 + s, 1 + n]);
            triangles.AddRange([last, top + n, top + s]);
        }
        for (var r = 0u; r + 1 < InnerRings; r++)
        {
            var a = 1 + (r * Segments);
            var b = a + Segments;
            for (var s = 0u; s < Segments; s++)
            {
                var n = (s + 1) % Segments;
                triangles.AddRange([a + s, b + n, a + n, a + s, b + s, b + n]);
            }
        }
        return [.. triangles];
    }
}
