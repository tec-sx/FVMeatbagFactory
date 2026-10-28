using System.Numerics;

namespace FVMeatbag.Rendering;

/// <summary>Per-frame camera, light and target information.</summary>
/// <param name="View">World to view, right-handed, System.Numerics row-vector convention.</param>
/// <param name="Projection">View to clip, right-handed with depth 0..1 and +Y up. Backends adjust for their clip space.</param>
/// <param name="DirectionToLight">Unit vector from the scene towards the directional light.</param>
/// <param name="LightRadiance">Light color × intensity.</param>
/// <param name="DisplayMode">How meshes are drawn.</param>
/// <param name="OverlaysOnTop">True draws overlays over the meshes even where a mesh is in front (X-ray).</param>
public readonly record struct FrameContext(
    uint Width,
    uint Height,
    Matrix4x4 View,
    Matrix4x4 Projection,
    Vector3 DirectionToLight,
    Vector3 LightRadiance,
    double TimeSeconds,
    MeshDisplayMode DisplayMode = MeshDisplayMode.Solid,
    bool OverlaysOnTop = true);

/// <summary>How the viewport draws meshes.</summary>
public enum MeshDisplayMode
{
    /// <summary>Edges only, including hidden ones.</summary>
    Wireframe,

    /// <summary>Lit clay surfaces.</summary>
    Solid,

    /// <summary>Lit clay surfaces with their visible edges on top.</summary>
    SolidWireframe,

    /// <summary>Material shading. Placeholder: draws like <see cref="Solid"/> until materials exist.</summary>
    Shaded,
}

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
    /// Draws <paramref name="draws"/>, then the see-through <paramref name="overlays"/>, then
    /// <paramref name="lines"/> on top of them, into the offscreen target and waits until the frame is finished.
    /// </summary>
    void RenderFrame(in FrameContext frame, IReadOnlyList<MeshDraw> draws, IReadOnlyList<OverlayDraw> overlays, IReadOnlyList<LineSegment> lines);

    /// <summary>
    /// Copies the last rendered frame as 8-bit BGRA rows (top row first) into
    /// <paramref name="destination"/>, which holds <see cref="Height"/> rows of <paramref name="rowBytes"/> bytes.
    /// </summary>
    void CopyPixels(nint destination, int rowBytes);
}
