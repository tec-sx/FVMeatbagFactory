namespace FVMeatbag.Core.Import;

/// <summary>Picks the importer for a mesh file by its extension.</summary>
public static class MeshImporter
{
    public static IReadOnlyList<string> SupportedExtensions { get; } =
        [.. GltfImporter.SupportedExtensions, .. FbxImporter.SupportedExtensions];

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static LoadedMesh Load(string path)
    {
        if (GltfImporter.IsSupported(path))
        {
            return GltfImporter.Load(path);
        }
        if (FbxImporter.IsSupported(path))
        {
            return FbxImporter.Load(path);
        }
        throw new NotSupportedException(
            $"{Path.GetFileName(path)} is not a supported mesh file ({string.Join(", ", SupportedExtensions)}).");
    }
}
