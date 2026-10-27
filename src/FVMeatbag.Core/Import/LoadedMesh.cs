using FVMeatbag.Graph;

namespace FVMeatbag.Core.Import;

/// <summary>A mesh read from disk, before it becomes part of a project.</summary>
/// <param name="Skeleton">The bones the geometry's <see cref="MeshGeometry.Skin"/> refers to, for skinned meshes.</param>
public sealed record LoadedMesh(string Name, string SourcePath, MeshGeometry Geometry, Maybe<Skeleton> Skeleton = default);
