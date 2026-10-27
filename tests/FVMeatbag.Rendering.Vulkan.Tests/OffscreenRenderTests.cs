using System.Numerics;
using System.Runtime.InteropServices;

namespace FVMeatbag.Rendering.Vulkan.Tests;

/// <summary>
/// Renders real frames. Needs a Vulkan driver; a software one (Mesa lavapipe) is enough. Skipped
/// on machines without one. Set FVMB_RENDER_DUMP to a folder to keep the frames as raw BGRA.
/// </summary>
public sealed class OffscreenRenderTests : IDisposable
{
    private const uint Size = 256;
    private static readonly Vector3 Eye = new(0, 0, 4);
    private readonly VulkanBackend _backend = new();

    public OffscreenRenderTests()
    {
        Assert.SkipUnless(VulkanBackend.IsLoaderAvailable(), "No Vulkan loader on this machine.");
        try
        {
            _backend.Initialize();
        }
        catch (InvalidOperationException e)
        {
            Assert.Skip($"No usable Vulkan device: {e.Message}");
        }
    }

    public void Dispose() => _backend.Dispose();

    [Fact]
    public void Empty_scene_is_cleared_to_the_background()
    {
        var pixels = Render([]);

        Assert.All(new[] { Pixel(pixels, 0, 0), Pixel(pixels, 128, 128), Pixel(pixels, 255, 255) }, p => Assert.Equal(Background, p));
    }

    [Fact]
    public void Lit_sphere_is_brighter_towards_the_light_and_upright()
    {
        // Light from the upper right front; the camera looks at the sphere from +Z.
        var pixels = Render([new MeshDraw(Sphere(Guid.NewGuid()), Matrix4x4.Identity)], toLight: Vector3.Normalize(new Vector3(1, 1, 1)));
        Dump(pixels, "sphere");

        Assert.Equal(Background, Pixel(pixels, 2, 2));
        Assert.NotEqual(Background, Pixel(pixels, 128, 128));
        Assert.True(Luma(Pixel(pixels, 170, 128)) > Luma(Pixel(pixels, 86, 128)), "Right side faces the light.");
        Assert.True(Luma(Pixel(pixels, 128, 86)) > Luma(Pixel(pixels, 128, 170)), "Top faces the light, so the image is not upside down.");
    }

    [Fact]
    public void Lines_draw_on_top_of_meshes()
    {
        // A vertical line through the middle of the sphere, starting behind it.
        var line = new LineSegment(new Vector3(0, -2, -2), new Vector3(0, 2, -2), new Vector3(1, 0, 0));

        var pixels = Render([new MeshDraw(Sphere(Guid.NewGuid()), Matrix4x4.Identity)], lines: [line]);
        Dump(pixels, "line");

        // The line falls on a pixel boundary, so MSAA may split it over two columns; one must be clearly red.
        Assert.Contains(Enumerable.Range(126, 4), x => IsRed(Pixel(pixels, x, 128)));
        Assert.False(IsRed(Pixel(pixels, 140, 128)));
    }

    [Fact]
    public void Wireframe_draws_only_the_edges()
    {
        Assert.SkipUnless(_backend.SupportsWireframe, "The device cannot draw polygons as lines.");
        var quad = Quad(Guid.NewGuid());

        var pixels = Render([new MeshDraw(quad, Matrix4x4.Identity)], mode: MeshDisplayMode.Wireframe);
        Dump(pixels, "wireframe");

        Assert.Equal(Background, Pixel(pixels, 128, 100)); // inside a face, off its edges
        Assert.Contains(Enumerable.Range(126, 5), x => Luma(Pixel(pixels, x, 128)) > 120); // the diagonal through the middle
    }

    [Fact]
    public void Solid_wireframe_draws_dark_edges_over_the_surface()
    {
        Assert.SkipUnless(_backend.SupportsWireframe, "The device cannot draw polygons as lines.");
        var quad = Quad(Guid.NewGuid());
        var solid = Render([new MeshDraw(quad, Matrix4x4.Identity)], mode: MeshDisplayMode.Solid);

        var pixels = Render([new MeshDraw(quad, Matrix4x4.Identity)], mode: MeshDisplayMode.SolidWireframe);
        Dump(pixels, "solid_wireframe");

        Assert.Equal(Pixel(solid, 128, 100), Pixel(pixels, 128, 100));
        Assert.Contains(Enumerable.Range(126, 5), x => Luma(Pixel(pixels, x, 128)) < Luma(Pixel(solid, x, 128)) - 40);
    }

    [Fact]
    public void Shaded_draws_like_solid_until_materials_exist()
    {
        var sphere = Sphere(Guid.NewGuid());

        var solid = Render([new MeshDraw(sphere, Matrix4x4.Identity)], mode: MeshDisplayMode.Solid);
        var shaded = Render([new MeshDraw(sphere, Matrix4x4.Identity)], mode: MeshDisplayMode.Shaded);

        Assert.Equal(solid, shaded);
    }

    [Fact]
    public void World_matrix_moves_the_mesh()
    {
        var sphere = Sphere(Guid.NewGuid());
        var pixels = Render([new MeshDraw(sphere, Matrix4x4.CreateScale(0.3f) * Matrix4x4.CreateTranslation(1, 0, 0))]);

        Assert.Equal(Background, Pixel(pixels, 128, 128));
        Assert.NotEqual(Background, Pixel(pixels, 128 + 88, 128)); // 1 unit at distance 4 with a 40° fov: 128 / (4 · tan 20°) px
    }

    [Fact]
    public void Resizing_and_new_mesh_versions_keep_rendering()
    {
        var id = Guid.NewGuid();
        Render([new MeshDraw(Sphere(id, version: 1), Matrix4x4.Identity)]);
        var pixels = Render([new MeshDraw(Sphere(id, version: 2, radius: 0.2f), Matrix4x4.Identity)], width: 128, height: 64);

        Assert.Equal(128u, _backend.Width);
        Assert.Equal(64u, _backend.Height);
        Assert.NotEqual(Background, Pixel(pixels, 64, 32, width: 128));
        Assert.Equal(Background, Pixel(pixels, 64, 6, width: 128)); // the smaller version is in use
    }

    private static uint Background => Pixel(RenderStatic(), 0, 0);

    private static byte[] RenderStatic()
    {
        using var backend = new VulkanBackend();
        backend.Initialize();
        return Render(backend, [], Size, Size, Vector3.UnitY);
    }

    private byte[] Render(
        IReadOnlyList<MeshDraw> draws,
        uint width = Size,
        uint height = Size,
        Vector3? toLight = null,
        IReadOnlyList<LineSegment>? lines = null,
        MeshDisplayMode mode = MeshDisplayMode.Solid) =>
        Render(_backend, draws, width, height, toLight ?? Vector3.Normalize(new Vector3(1, 1, 1)), lines, mode);

    private static byte[] Render(
        VulkanBackend backend,
        IReadOnlyList<MeshDraw> draws,
        uint width,
        uint height,
        Vector3 toLight,
        IReadOnlyList<LineSegment>? lines = null,
        MeshDisplayMode mode = MeshDisplayMode.Solid)
    {
        var frame = new FrameContext(
            width,
            height,
            Matrix4x4.CreateLookAt(Eye, Vector3.Zero, Vector3.UnitY),
            Matrix4x4.CreatePerspectiveFieldOfView(float.DegreesToRadians(40), (float)width / height, 0.01f, 100f),
            toLight,
            new Vector3(3f),
            0,
            mode);
        backend.RenderFrame(frame, draws, lines ?? []);
        var pixels = new byte[width * height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            backend.CopyPixels(handle.AddrOfPinnedObject(), (int)width * 4);
        }
        finally
        {
            handle.Free();
        }
        return pixels;
    }

    private static uint Pixel(byte[] bgra, int x, int y, int width = (int)Size) => BitConverter.ToUInt32(bgra, ((y * width) + x) * 4);

    private static bool IsRed(uint bgra) => ((bgra >> 16) & 0xFF) > 200 && ((bgra >> 8) & 0xFF) < 120 && (bgra & 0xFF) < 120;

    private static float Luma(uint bgra) => (0.11f * (bgra & 0xFF)) + (0.59f * ((bgra >> 8) & 0xFF)) + (0.3f * ((bgra >> 16) & 0xFF));

    private static void Dump(byte[] pixels, string name)
    {
        if (Environment.GetEnvironmentVariable("FVMB_RENDER_DUMP") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, $"{name}_{Size}x{Size}.bgra"), pixels);
        }
    }

    /// <summary>A 2×2 square facing +Z, split along the diagonal from its lower left to its upper right corner.</summary>
    private static RenderMesh Quad(Guid id)
    {
        Vector3[] positions = [new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0)];
        Vector3[] normals = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ];
        return new RenderMesh(id, 1, positions, normals, new uint[] { 0, 1, 2, 0, 2, 3 });
    }

    private static RenderMesh Sphere(Guid id, long version = 1, float radius = 1f, int rings = 24, int segments = 48)
    {
        var positions = new List<Vector3>();
        var indices = new List<uint>();
        for (var r = 0; r <= rings; r++)
        {
            var phi = MathF.PI * r / rings;
            for (var s = 0; s <= segments; s++)
            {
                var theta = 2 * MathF.PI * s / segments;
                positions.Add(new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta)));
            }
        }
        for (var r = 0; r < rings; r++)
        {
            for (var s = 0; s < segments; s++)
            {
                var a = (uint)((r * (segments + 1)) + s);
                var b = a + (uint)segments + 1;
                indices.AddRange([a, a + 1, b, a + 1, b + 1, b]); // counter-clockwise seen from outside, as glTF requires
            }
        }
        var normals = positions.ToArray();
        return new RenderMesh(id, version, positions.Select(p => p * radius).ToArray(), normals, indices.ToArray());
    }
}
