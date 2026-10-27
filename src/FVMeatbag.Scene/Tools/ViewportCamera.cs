using System.Numerics;

namespace FVMeatbag.Scene.Tools;

/// <summary>
/// What a viewport tool needs to know about the camera: matrices in System.Numerics convention
/// (clip +Y up, depth 0..1) and the viewport size in the same units as pointer positions.
/// </summary>
public readonly record struct ViewportCamera(Matrix4x4 View, Matrix4x4 Projection, Vector3 Eye, Vector2 Size)
{
    public Matrix4x4 ViewProjection => View * Projection;

    /// <summary>Unit vector the camera looks along.</summary>
    public Vector3 Forward => Vector3.Normalize(new Vector3(-View.M13, -View.M23, -View.M33));

    /// <summary>Screen position of a world point (origin top left), or null when it is behind the camera.</summary>
    public Vector2? ToScreen(Vector3 world) => BonePicker.ToScreen(world, ViewProjection, Size);

    /// <summary>The ray from the camera through a screen point.</summary>
    public (Vector3 Origin, Vector3 Direction) Ray(Vector2 point)
    {
        if (!Matrix4x4.Invert(ViewProjection, out var inverse))
        {
            return (Eye, Forward);
        }
        var x = (2f * point.X / Size.X) - 1f;
        var y = 1f - (2f * point.Y / Size.Y);
        var near = Unproject(new Vector4(x, y, 0f, 1f), inverse);
        var far = Unproject(new Vector4(x, y, 1f, 1f), inverse);
        return (near, Vector3.Normalize(far - near));
    }

    /// <summary>World units per pixel at the depth of <paramref name="world"/>.</summary>
    public float WorldPerPixel(Vector3 world)
    {
        var depth = MathF.Max(Vector3.Dot(world - Eye, Forward), 1e-4f);
        return 2f * depth / (MathF.Max(Projection.M22, 1e-6f) * MathF.Max(Size.Y, 1f));
    }

    private static Vector3 Unproject(Vector4 clip, Matrix4x4 inverse)
    {
        var world = Vector4.Transform(clip, inverse);
        return new Vector3(world.X, world.Y, world.Z) / world.W;
    }
}
