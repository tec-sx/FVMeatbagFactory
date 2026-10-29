using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Muscles;

namespace FVMeatbag.Scene.Tests;

/// <summary>Rays to the skin and the distance to it, which place and fit the muscles.</summary>
public sealed class SkinSurfaceTests
{
    [Fact]
    public void Rays_from_inside_a_cube_hit_its_faces()
    {
        var skin = Cube();

        Assert.Equal(12, skin.TriangleCount);
        foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ })
        {
            Assert.Equal(0.5f, skin.RayCast(Vector3.Zero, direction).Value, 1e-5f);
        }
        Assert.Equal(0.25f, skin.RayCast(new Vector3(0.25f, 0, 0), Vector3.UnitX).Value, 1e-5f);
        Assert.Equal(new Vector3(0.5f, 0.2f, 0), skin.RayHit(new Vector3(0, 0.2f, 0), new Vector3(3, 0, 0)).Value);
        Assert.True(skin.RayCast(new Vector3(2, 0, 0), Vector3.UnitX).IsNone); // pointing away
        Assert.Equal(1.5f, skin.Distance(new Vector3(2, 0, 0)).Value, 1e-5f);
        Assert.Equal(0.5f, skin.Distance(Vector3.Zero).Value, 1e-5f);
        Assert.Equal(MathF.Sqrt(3f) * 0.5f, skin.Distance(Vector3.One).Value, 1e-5f);
    }

    [Fact]
    public void Tree_finds_what_a_brute_force_search_finds_on_the_demo_mesh()
    {
        var geometry = GltfImporter.Load(SkeletonImportTests.SamplePath("Demo", "Meshes", "MannyBaseMesh.glb")).Geometry;
        var skin = SkinSurface.From(geometry);
        var positions = geometry.Positions.ToArray();
        var indices = geometry.Indices.ToArray();
        var triangles = Enumerable.Range(0, indices.Length / 3)
            .Select(i => new SkinSurface([positions[indices[i * 3]], positions[indices[(i * 3) + 1]], positions[indices[(i * 3) + 2]]], [0u, 1u, 2u]))
            .ToArray();
        var random = new Random(7);
        var bounds = geometry.Bounds;
        for (var n = 0; n < 40; n++)
        {
            var point = Vector3.Lerp(bounds.Min, bounds.Max, new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle()));
            var direction = Vector3.Normalize(new Vector3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, random.NextSingle() - 0.5f));
            var nearest = float.PositiveInfinity;
            var first = float.PositiveInfinity;
            foreach (var one in triangles)
            {
                nearest = MathF.Min(nearest, one.Distance(point).Value);
                if (one.RayCast(point, direction).TryGetValue(out var t))
                {
                    first = MathF.Min(first, t);
                }
            }
            Assert.Equal(nearest, skin.Distance(point).Value, 1e-4f);
            Assert.Equal(float.IsPositiveInfinity(first), skin.RayCast(point, direction).IsNone);
            if (!float.IsPositiveInfinity(first))
            {
                Assert.Equal(first, skin.RayCast(point, direction).Value, 1e-4f);
            }
        }
    }

    private static SkinSurface Cube()
    {
        var corners = new Vector3[8];
        for (var i = 0; i < 8; i++)
        {
            corners[i] = new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f);
        }
        uint[] faces =
        [
            0, 2, 1, 1, 2, 3, // -Z
            4, 5, 6, 5, 7, 6, // +Z
            0, 1, 4, 1, 5, 4, // -Y
            2, 6, 3, 3, 6, 7, // +Y
            0, 4, 2, 2, 4, 6, // -X
            1, 3, 5, 3, 7, 5, // +X
        ];
        return new SkinSurface(corners, faces);
    }
}
