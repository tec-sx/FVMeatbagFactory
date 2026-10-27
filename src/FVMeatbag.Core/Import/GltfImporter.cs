using System.Numerics;
using FVMeatbag.Graph;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;
using Node = SharpGLTF.Schema2.Node;

namespace FVMeatbag.Core.Import;

/// <summary>
/// Reads glTF 2.0 files (.glb or .gltf) into a single <see cref="MeshGeometry"/>. Every mesh in the
/// file is baked into one triangle list in world space. Skinned meshes are deformed into the rest
/// pose the file shows, and their joints become one <see cref="Skeleton"/> with per-vertex
/// <see cref="SkinWeights"/>. Materials and animations are ignored for now.
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
        var (geometry, skeleton) = Bake(model);
        return new LoadedMesh(Path.GetFileNameWithoutExtension(path), path, geometry, skeleton);
    }

    private static (MeshGeometry Geometry, Maybe<Skeleton> Skeleton) Bake(ModelRoot model)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<uint>();
        var skin = new List<SkinWeights>();

        var scene = model.DefaultScene ?? (model.LogicalScenes.Count > 0 ? model.LogicalScenes[0] : null);
        var allNodes = scene is null ? [.. model.LogicalNodes] : scene.VisualChildren.SelectMany(Flatten).ToList();
        var meshNodes = allNodes.Where(n => n.Mesh is not null).ToList();
        var (skeleton, boneOf) = BuildSkeleton(allNodes, meshNodes);

        foreach (var node in meshNodes)
        {
            var world = node.WorldMatrix;
            var normalMatrix = NormalMatrix(world);
            // A node that is not skinned but hangs below a bone (a hat, an eye) follows that bone rigidly.
            var rigidBone = Ancestors(node).Select(a => boneOf.GetValueOrDefault(a, -1)).FirstOrDefault(b => b >= 0, -1);

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
                var influences = node.Skin is null ? null : ReadInfluences(primitive, node.Skin, boneOf, local.Length);

                var offset = (uint)positions.Count;
                for (var i = 0; i < local.Length; i++)
                {
                    Vector3 position, normal;
                    if (influences is not null)
                    {
                        // glTF ignores a skinned node's own transform; the joints place the vertices.
                        // Deform into the rest pose so the stored mesh is what the file shows.
                        var blended = Blend(influences[i]);
                        position = Vector3.Transform(local[i], blended);
                        normal = Vector3.TransformNormal(localNormals[i], NormalMatrix(blended));
                        skin.Add(SkinWeights.FromInfluences(influences[i].Select(x => (x.Bone, x.Weight))));
                    }
                    else
                    {
                        position = Vector3.Transform(local[i], world);
                        normal = Vector3.TransformNormal(localNormals[i], normalMatrix);
                        skin.Add(rigidBone >= 0 ? SkinWeights.FromInfluences([(rigidBone, 1f)]) : SkinWeights.None);
                    }
                    positions.Add(position);
                    normals.Add(normal == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(normal));
                }
                indices.AddRange(localIndices.Select(i => i + offset));
            }
        }

        if (indices.Count == 0)
        {
            throw new InvalidDataException("The file contains no triangle meshes.");
        }
        var geometry = new MeshGeometry([.. positions], [.. normals], [.. indices], skeleton.HasValue ? [.. skin] : null);
        return (geometry, skeleton);
    }

    /// <summary>
    /// One skeleton for the whole file: every joint of every skin used by a visible mesh, parents
    /// first. A bone's parent is its nearest ancestor that is also a joint.
    /// </summary>
    private static (Maybe<Skeleton> Skeleton, Dictionary<Node, int> BoneOf) BuildSkeleton(List<Node> sceneOrder, List<Node> meshNodes)
    {
        var joints = new HashSet<Node>();
        foreach (var skin in meshNodes.Select(n => n.Skin).Where(s => s is not null).Distinct())
        {
            for (var j = 0; j < skin.JointsCount; j++)
            {
                joints.Add(skin.GetJoint(j).Joint);
            }
        }
        var boneOf = new Dictionary<Node, int>();
        if (joints.Count == 0)
        {
            return (Maybe<Skeleton>.None, boneOf);
        }

        // Scene order is depth first, so parents come before children. Joints outside the scene go last.
        var ordered = sceneOrder.Where(joints.Contains).Concat(joints.Where(j => !sceneOrder.Contains(j))).Distinct().ToList();
        var bones = new List<Bone>();
        var names = new HashSet<string>();
        foreach (var node in ordered)
        {
            var parentNode = Ancestors(node).FirstOrDefault(boneOf.ContainsKey);
            var parent = parentNode is null ? -1 : boneOf[parentNode];
            var local = parent < 0 || !Matrix4x4.Invert(parentNode!.WorldMatrix, out var parentInverse)
                ? node.WorldMatrix
                : node.WorldMatrix * parentInverse;
            var name = string.IsNullOrWhiteSpace(node.Name) || !names.Add(node.Name) ? $"Bone{bones.Count}" : node.Name;
            names.Add(name);
            boneOf[node] = bones.Count;
            bones.Add(new Bone(name, parent, local));
        }
        return (new Skeleton(bones), boneOf);
    }

    private static List<(int Bone, float Weight, Matrix4x4 Skinning)>[] ReadInfluences(
        MeshPrimitive primitive, Skin skin, Dictionary<Node, int> boneOf, int vertexCount)
    {
        var result = new List<(int, float, Matrix4x4)>[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            result[i] = [];
        }
        for (var set = 0; ; set++)
        {
            var joints = primitive.GetVertexAccessor($"JOINTS_{set}")?.AsVector4Array();
            var weights = primitive.GetVertexAccessor($"WEIGHTS_{set}")?.AsVector4Array();
            if (joints is null || weights is null)
            {
                break;
            }
            for (var i = 0; i < vertexCount; i++)
            {
                var j = joints[i];
                var w = weights[i];
                Add(i, j.X, w.X);
                Add(i, j.Y, w.Y);
                Add(i, j.Z, w.Z);
                Add(i, j.W, w.W);
            }
        }
        return result;

        void Add(int vertex, float joint, float weight)
        {
            if (weight <= 0f || joint < 0 || joint >= skin.JointsCount)
            {
                return;
            }
            var (node, inverseBind) = skin.GetJoint((int)joint);
            // Row vectors: into the joint's bind space, then out through the joint's rest transform.
            result[vertex].Add((boneOf[node], weight, inverseBind * node.WorldMatrix));
        }
    }

    private static Matrix4x4 Blend(List<(int Bone, float Weight, Matrix4x4 Skinning)> influences)
    {
        var total = influences.Sum(i => i.Weight);
        if (total <= 0f)
        {
            return Matrix4x4.Identity;
        }
        var blended = default(Matrix4x4);
        foreach (var (_, weight, skinning) in influences)
        {
            blended += skinning * (weight / total);
        }
        return blended;
    }

    private static Matrix4x4 NormalMatrix(Matrix4x4 m) =>
        Matrix4x4.Invert(m, out var inverse) ? Matrix4x4.Transpose(inverse) : Matrix4x4.Identity;

    private static IEnumerable<Node> Ancestors(Node node)
    {
        for (var parent = node.VisualParent; parent is not null; parent = parent.VisualParent)
        {
            yield return parent;
        }
    }

    private static IEnumerable<Node> Flatten(Node node) => node.VisualChildren.SelectMany(Flatten).Prepend(node);
}
