using System.Numerics;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;

namespace FVMeatbag.Core.Import;

/// <summary>
/// Reads glTF 2.0 files (.glb or .gltf) into a single <see cref="MeshGeometry"/>. Every mesh in the
/// file is baked into one triangle list in world space, which is what the viewport needs for now.
/// Materials are ignored; skeletons and skin weights come with the skeletal import milestone.
/// </summary>
public static class GltfImporter
{
    public static IReadOnlyList<string> SupportedExtensions { get; } = [".glb", ".gltf"];

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static LoadedMesh Load(string path)
    {
        if (!IsSupported(path))
        {
            throw new NotSupportedException($"{Path.GetFileName(path)} is not a glTF file (.glb or .gltf).");
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{Path.GetFileName(path)} does not exist.", path);
        }
        // Exporters routinely write files with small spec violations; repair what can be repaired.
        var model = ModelRoot.Load(path, new ReadSettings { Validation = ValidationMode.TryFix });
        return new LoadedMesh(Path.GetFileNameWithoutExtension(path), path, Bake(model));
    }

    private static MeshGeometry Bake(ModelRoot model)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<uint>();

        var scene = model.DefaultScene ?? (model.LogicalScenes.Count > 0 ? model.LogicalScenes[0] : null);
        var nodes = scene is null
            ? model.LogicalNodes.Where(n => n.Mesh is not null)
            : scene.VisualChildren.SelectMany(Flatten).Where(n => n.Mesh is not null);

        foreach (var node in nodes)
        {
            // glTF ignores the node transform of skinned meshes; until poses exist, show the bind pose as authored.
            var world = node.Skin is null ? node.WorldMatrix : Matrix4x4.Identity;
            var normalMatrix = Matrix4x4.Invert(world, out var inverse) ? Matrix4x4.Transpose(inverse) : Matrix4x4.Identity;

            foreach (var primitive in node.Mesh.Primitives)
            {
                var source = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
                if (source is null || source.Count == 0)
                {
                    continue;
                }
                var triangles = primitive.GetTriangleIndices().ToList();
                if (triangles.Count == 0)
                {
                    continue; // points and lines
                }

                var local = source.ToArray();
                var localIndices = triangles.SelectMany(t => new[] { (uint)t.A, (uint)t.B, (uint)t.C }).ToArray();
                var localNormals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array().ToArray()
                    ?? MeshGeometry.ComputeNormals(local, localIndices);

                var offset = (uint)positions.Count;
                for (var i = 0; i < local.Length; i++)
                {
                    positions.Add(Vector3.Transform(local[i], world));
                    var n = Vector3.TransformNormal(localNormals[i], normalMatrix);
                    normals.Add(n == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(n));
                }
                indices.AddRange(localIndices.Select(i => i + offset));
            }
        }

        if (indices.Count == 0)
        {
            throw new InvalidDataException("The file contains no triangle meshes.");
        }
        return new MeshGeometry([.. positions], [.. normals], [.. indices]);
    }

    private static IEnumerable<Node> Flatten(Node node) => node.VisualChildren.SelectMany(Flatten).Prepend(node);
}
