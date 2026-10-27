using System.Numerics;

namespace FVMeatbag.Rendering;

/// <summary>Per-frame camera and target information.</summary>
public readonly record struct FrameContext(uint Width, uint Height, Matrix4x4 View, Matrix4x4 Projection, double TimeSeconds);

/// <summary>
/// A GPU backend. The app renders into an offscreen target owned by the backend and the viewport
/// control presents it, so the backend never needs to know about Avalonia windows.
/// </summary>
public interface IRenderBackend : IDisposable
{
    string Name { get; }
    bool IsInitialized { get; }

    void Initialize();
    void Resize(uint width, uint height);
    void RenderFrame(in FrameContext frame);
}
