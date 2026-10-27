namespace FVMeatbag.Core.Import;

/// <summary>
/// A skeletal mesh read from disk, before it becomes part of a project. Placeholder: geometry,
/// skeleton and skin weights are added with the real importer.
/// </summary>
public sealed record LoadedMesh(string Name, string SourcePath);
