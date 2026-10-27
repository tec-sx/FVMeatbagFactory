namespace FVMeatbag.Core.Import;

/// <summary>
/// A mesh read from disk, before it becomes part of a project. Skeleton and skin weights are
/// added with the skeletal import milestone.
/// </summary>
public sealed record LoadedMesh(string Name, string SourcePath, MeshGeometry Geometry);
