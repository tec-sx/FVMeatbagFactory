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
