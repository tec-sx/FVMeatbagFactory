using System.Numerics;

namespace FVMeatbag.Rendering;

/// <summary>
/// Geometry handed to a backend. Backends upload it once and keep it while draws keep referring to
/// the same <see cref="Id"/> and <see cref="Version"/>; a new version replaces the upload.
/// </summary>
public sealed record RenderMesh(
    Guid Id,
    long Version,
    ReadOnlyMemory<Vector3> Positions,
    ReadOnlyMemory<Vector3> Normals,
    ReadOnlyMemory<uint> Indices);

/// <summary>One mesh placed in the world for one frame.</summary>
public readonly record struct MeshDraw(RenderMesh Mesh, Matrix4x4 World);

/// <summary>
/// A see-through mesh drawn after the opaque ones, such as a muscle in the preview. Overlays hide
/// each other where they overlap, and are blended over what is behind them by
/// <see cref="Color"/>'s alpha.
/// </summary>
/// <param name="Color">Display (sRGB) color, 0..1; alpha is the opacity.</param>
public readonly record struct OverlayDraw(RenderMesh Mesh, Matrix4x4 World, Vector4 Color);

/// <summary>An overlay line drawn on top of the meshes, such as a bone.</summary>
/// <param name="Color">Display (sRGB) color, 0..1.</param>
public readonly record struct LineSegment(Vector3 From, Vector3 To, Vector3 Color);
