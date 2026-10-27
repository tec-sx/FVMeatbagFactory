using System.Numerics;

namespace FVMeatbag.Rendering;

/// <summary>Per-frame camera, light and target information.</summary>
/// <param name="View">World to view, right-handed, System.Numerics row-vector convention.</param>
/// <param name="Projection">View to clip, right-handed with depth 0..1 and +Y up. Backends adjust for their clip space.</param>
/// <param name="DirectionToLight">Unit vector from the scene towards the directional light.</param>
/// <param name="LightRadiance">Light color × intensity.</param>
public readonly record struct FrameContext(
    uint Width,
    uint Height,
    Matrix4x4 View,
    Matrix4x4 Projection,
    Vector3 DirectionToLight,
    Vector3 LightRadiance,
    double TimeSeconds);

/// <summary>
/// A GPU backend. The app renders into an offscreen target owned by the backend and the viewport
/// control presents it, so the backend never needs to know about Avalonia windows.
/// </summary>
public interface IRenderBackend : IDisposable
{
    string Name { get; }
    bool IsInitialized { get; }

    /// <summary>Size of the offscreen target, set by <see cref="Resize"/>.</summary>
    uint Width { get; }
    uint Height { get; }

    void Initialize();
    void Resize(uint width, uint height);

    /// <summary>
    /// Draws <paramref name="draws"/>, then <paramref name="lines"/> on top of them, into the
    /// offscreen target and waits until the frame is finished.
    /// </summary>
    void RenderFrame(in FrameContext frame, IReadOnlyList<MeshDraw> draws, IReadOnlyList<LineSegment> lines);

    /// <summary>
    /// Copies the last rendered frame as 8-bit BGRA rows (top row first) into
    /// <paramref name="destination"/>, which holds <see cref="Height"/> rows of <paramref name="rowBytes"/> bytes.
    /// </summary>
    void CopyPixels(nint destination, int rowBytes);
}
