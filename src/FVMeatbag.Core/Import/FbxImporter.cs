using System.Numerics;
using System.Runtime.InteropServices;
using FVMeatbag.Graph;
using Silk.NET.Assimp;
using AssimpNode = Silk.NET.Assimp.Node;
using AssimpScene = Silk.NET.Assimp.Scene;

namespace FVMeatbag.Core.Import;

/// <summary>
/// Reads Autodesk FBX files (binary or ASCII) through the native Assimp library into a single
/// <see cref="MeshGeometry"/>, the same way <see cref="GltfImporter"/> does: every mesh is baked into
/// one triangle list in the rest pose, and the skin's bones become one <see cref="Skeleton"/> with
/// per-vertex <see cref="SkinWeights"/>. Materials and animations are ignored for now.
/// </summary>
/// <remarks>
/// Assimp converts the file's axis system to +Y up but keeps its units, so the scene is scaled
/// to meters here from the file's UnitScaleFactor (centimeters per unit).
/// <para>
/// FBX does not say which nodes are joints, so the skeleton is: every bone that weights a vertex,
/// every node between two such bones, and every node below a bone that carries no mesh (end joints
/// such as head_end). A mesh that is not skinned but hangs below a joint follows that joint rigidly.
/// A file saved in a pose imports in that pose, which becomes the rest pose.
/// </para>
/// </remarks>
public static class FbxImporter
{
    private const PostProcessSteps Steps =
        PostProcessSteps.Triangulate
        | PostProcessSteps.JoinIdenticalVertices
        | PostProcessSteps.GenerateSmoothNormals;

    private static readonly Lazy<Assimp> Api = new(LoadApi);

    public static IReadOnlyList<string> SupportedExtensions { get; } = [".fbx"];

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads the Assimp library that ships with the app. Silk.NET finds it through the runtime's
    /// RID fallback graph, which Microsoft's .NET builds no longer carry, so the app's own
    /// runtimes folder is tried first.
    /// </summary>
    private static Assimp LoadApi()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeId(), "native", LibraryName());
        return System.IO.File.Exists(bundled) ? new Assimp(Assimp.CreateDefaultContext([bundled])) : Assimp.GetApi();
    }

    private static string RuntimeId()
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        return $"{os}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
    }

    private static string LibraryName() =>
        OperatingSystem.IsWindows() ? (Environment.Is64BitProcess ? "Assimp64.dll" : "Assimp32.dll")
        : OperatingSystem.IsMacOS() ? "libassimp.5.dylib"
        : "libassimp.so.5";

    public static LoadedMesh Load(string path)
    {
        if (!IsSupported(path))
        {
            throw new NotSupportedException($"{Path.GetFileName(path)} is not an FBX file (.fbx).");
        }
        if (!System.IO.File.Exists(path))
        {
            throw new FileNotFoundException($"{Path.GetFileName(path)} does not exist.", path);
        }
        var (nodes, meshes) = Read(path);
        var (geometry, skeleton) = Bake(nodes, meshes);
        return new LoadedMesh(Path.GetFileNameWithoutExtension(path), path, geometry, skeleton);
    }

    /// <summary>A node of the file with its world transform, already in meters.</summary>
    private sealed class SceneNode(string name, Matrix4x4 world, SceneNode? parent, int[] meshes)
    {
        public string Name { get; } = name;
        public Matrix4x4 World { get; } = world;
        public SceneNode? Parent { get; } = parent;
        public int[] Meshes { get; } = meshes;
        public List<SceneNode> Children { get; } = [];

        public IEnumerable<SceneNode> Ancestors()
        {
            for (var node = Parent; node is not null; node = node.Parent)
            {
                yield return node;
            }
        }

        public IEnumerable<SceneNode> Subtree() => Children.SelectMany(c => c.Subtree()).Prepend(this);
    }

    /// <param name="Offset">Takes a mesh vertex into the bone's space at bind time.</param>
    private sealed record SceneBone(string Name, Matrix4x4 Offset, (int Vertex, float Weight)[] Weights);

    private sealed record SceneMesh(Vector3[] Positions, Vector3[]? Normals, uint[] Indices, SceneBone[] Bones);

    /// <summary>Copies what the importer needs out of Assimp's scene, so only this method touches native memory.</summary>
    private static unsafe (List<SceneNode> Nodes, List<SceneMesh> Meshes) Read(string path)
    {
        Assimp ai;
        try
        {
            ai = Api.Value;
        }
        catch (Exception e) when (e is DllNotFoundException or FileNotFoundException or BadImageFormatException)
        {
            throw new NotSupportedException($"The FBX reader (Assimp) could not be loaded: {e.Message}", e);
        }

        var store = ai.CreatePropertyStore();
        try
        {
            // Fold FBX pivot helper nodes into their owners so bones stay single nodes.
            ai.SetImportPropertyInteger(store, "IMPORT_FBX_PRESERVE_PIVOTS", 0);
            ai.SetImportPropertyInteger(store, "IMPORT_FBX_READ_ANIMATIONS", 0);
            var scene = ai.ImportFileExWithProperties(path, (uint)Steps, (FileIO*)null, store);
            if (scene is null)
            {
                throw new InvalidDataException($"{Path.GetFileName(path)} could not be read: {ai.GetErrorStringS()}");
            }
            try
            {
                var nodes = new List<SceneNode>();
                Walk(scene->MRootNode, null, Matrix4x4.CreateScale(MetersPerUnit(scene)), nodes);
                var meshes = new List<SceneMesh>();
                for (var m = 0; m < scene->MNumMeshes; m++)
                {
                    meshes.Add(ReadMesh(scene->MMeshes[m]));
                }
                return (nodes, meshes);
            }
            finally
            {
                ai.ReleaseImport(scene);
            }
        }
        finally
        {
            ai.ReleasePropertyStore(store);
        }
    }

    /// <summary>Depth first, so parents come before children.</summary>
    private static unsafe void Walk(AssimpNode* node, SceneNode? parent, Matrix4x4 parentWorld, List<SceneNode> nodes)
    {
        // Assimp stores column-vector matrices; System.Numerics uses row vectors.
        var world = Matrix4x4.Transpose(node->MTransformation) * parentWorld;
        var meshes = new int[node->MNumMeshes];
        for (var i = 0; i < meshes.Length; i++)
        {
            meshes[i] = (int)node->MMeshes[i];
        }
        var current = new SceneNode(node->MName.AsString, world, parent, meshes);
        parent?.Children.Add(current);
        nodes.Add(current);
        for (var i = 0; i < node->MNumChildren; i++)
        {
            Walk(node->MChildren[i], current, world, nodes);
        }
    }

    private static unsafe SceneMesh ReadMesh(Mesh* mesh)
    {
        var count = (int)mesh->MNumVertices;
        var positions = new ReadOnlySpan<Vector3>(mesh->MVertices, count).ToArray();
        var normals = mesh->MNormals is null ? null : new ReadOnlySpan<Vector3>(mesh->MNormals, count).ToArray();

        var indices = new List<uint>((int)mesh->MNumFaces * 3);
        for (var f = 0; f < mesh->MNumFaces; f++)
        {
            var face = mesh->MFaces[f];
            if (face.MNumIndices == 3) // points and lines stay out
            {
                indices.Add(face.MIndices[0]);
                indices.Add(face.MIndices[1]);
                indices.Add(face.MIndices[2]);
            }
        }

        var bones = new SceneBone[mesh->MNumBones];
        for (var b = 0; b < bones.Length; b++)
        {
            var bone = mesh->MBones[b];
            var weights = new (int, float)[bone->MNumWeights];
            for (var w = 0; w < weights.Length; w++)
            {
                weights[w] = ((int)bone->MWeights[w].MVertexId, bone->MWeights[w].MWeight);
            }
            bones[b] = new SceneBone(bone->MName.AsString, Matrix4x4.Transpose(bone->MOffsetMatrix), weights);
        }
        return new SceneMesh(positions, normals, [.. indices], bones);
    }

    /// <summary>FBX records its unit as centimeters per file unit; 1 means the file is in centimeters.</summary>
    private static unsafe float MetersPerUnit(AssimpScene* scene)
    {
        var metadata = scene->MMetaData;
        if (metadata is null)
        {
            return 0.01f;
        }
        for (var i = 0; i < metadata->MNumProperties; i++)
        {
            if (metadata->MKeys[i].AsString != "UnitScaleFactor")
            {
                continue;
            }
            var entry = metadata->MValues[i];
            double centimeters = entry.MType switch
            {
                MetadataType.Float => *(float*)entry.MData,
                MetadataType.Double => *(double*)entry.MData,
                MetadataType.Int32 => *(int*)entry.MData,
                _ => 1,
            };
            return centimeters > 0 ? (float)(centimeters / 100) : 0.01f;
        }
        return 0.01f;
    }

    private static (MeshGeometry Geometry, Maybe<Skeleton> Skeleton) Bake(List<SceneNode> nodes, List<SceneMesh> meshes)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<uint>();
        var skin = new List<SkinWeights>();

        var meshNodes = nodes.Where(n => n.Meshes.Length > 0).ToList();
        var nodeByName = new Dictionary<string, SceneNode>();
        foreach (var node in nodes)
        {
            nodeByName.TryAdd(node.Name, node);
        }
        var (skeleton, boneOf) = BuildSkeleton(nodes, meshNodes, meshes, nodeByName);

        foreach (var node in meshNodes)
        {
            var world = node.World;
            var normalMatrix = NormalMatrix(world);
            var rigidBone = node.Ancestors().Select(a => boneOf.GetValueOrDefault(a, -1)).FirstOrDefault(b => b >= 0, -1);

            foreach (var mesh in node.Meshes.Select(i => meshes[i]))
            {
                if (mesh.Positions.Length == 0 || mesh.Indices.Length == 0)
                {
                    continue;
                }
                var localNormals = mesh.Normals ?? MeshGeometry.ComputeNormals(mesh.Positions, mesh.Indices);
                var influences = mesh.Bones.Length == 0 ? null : ReadInfluences(mesh, boneOf, nodeByName);
                var bindSpace = influences is null ? Matrix4x4.Identity : ChooseBindSpace(mesh.Positions, influences, node, nodes[0]);

                var offset = (uint)positions.Count;
                for (var i = 0; i < mesh.Positions.Length; i++)
                {
                    Vector3 position, normal;
                    if (influences is not null && influences[i].Count > 0)
                    {
                        // Deform into the rest pose the file shows, so bind pose and rest pose agree.
                        var blended = bindSpace * Blend(influences[i]);
                        position = Vector3.Transform(mesh.Positions[i], blended);
                        normal = Vector3.TransformNormal(localNormals[i], NormalMatrix(blended));
                        skin.Add(SkinWeights.FromInfluences(influences[i].Select(x => (x.Bone, x.Weight))));
                    }
                    else
                    {
                        position = Vector3.Transform(mesh.Positions[i], world);
                        normal = Vector3.TransformNormal(localNormals[i], normalMatrix);
                        skin.Add(rigidBone >= 0 ? SkinWeights.FromInfluences([(rigidBone, 1f)]) : SkinWeights.None);
                    }
                    positions.Add(position);
                    normals.Add(normal == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(normal));
                }
                indices.AddRange(mesh.Indices.Select(i => i + offset));
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
    /// One skeleton for the whole file, parents first. A bone's parent is its nearest ancestor that
    /// is also a joint, and its rest transform is taken from the node's world transform.
    /// </summary>
    private static (Maybe<Skeleton> Skeleton, Dictionary<SceneNode, int> BoneOf) BuildSkeleton(
        List<SceneNode> sceneOrder, List<SceneNode> meshNodes, List<SceneMesh> meshes, Dictionary<string, SceneNode> nodeByName)
    {
        var weighted = meshNodes
            .SelectMany(n => n.Meshes)
            .SelectMany(m => meshes[m].Bones)
            .Select(b => nodeByName.GetValueOrDefault(b.Name))
            .OfType<SceneNode>()
            .ToHashSet();
        var boneOf = new Dictionary<SceneNode, int>();
        if (weighted.Count == 0)
        {
            return (Maybe<Skeleton>.None, boneOf);
        }

        var bones = new List<Bone>();
        var names = new HashSet<string>();
        foreach (var node in sceneOrder.Where(IsJoint))
        {
            var parentNode = node.Ancestors().FirstOrDefault(boneOf.ContainsKey);
            var parent = parentNode is null ? -1 : boneOf[parentNode];
            var local = parent < 0 || !Matrix4x4.Invert(parentNode!.World, out var parentInverse)
                ? node.World
                : node.World * parentInverse;
            var name = string.IsNullOrWhiteSpace(node.Name) || !names.Add(node.Name) ? $"Bone{bones.Count}" : node.Name;
            names.Add(name);
            boneOf[node] = bones.Count;
            bones.Add(new Bone(name, parent, local));
        }
        return (new Skeleton(bones), boneOf);

        bool IsJoint(SceneNode node)
        {
            if (weighted.Contains(node))
            {
                return true;
            }
            if (!node.Ancestors().Any(weighted.Contains))
            {
                return false;
            }
            var below = node.Subtree().ToList();
            return below.Any(weighted.Contains) || !below.Any(n => n.Meshes.Length > 0);
        }
    }

    private static List<(int Bone, float Weight, Matrix4x4 Skinning)>[] ReadInfluences(
        SceneMesh mesh, Dictionary<SceneNode, int> boneOf, Dictionary<string, SceneNode> nodeByName)
    {
        var result = new List<(int, float, Matrix4x4)>[mesh.Positions.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = [];
        }
        foreach (var bone in mesh.Bones)
        {
            if (!nodeByName.TryGetValue(bone.Name, out var node) || !boneOf.TryGetValue(node, out var index))
            {
                continue;
            }
            // Row vectors: into the bone's bind space, then out through the bone's rest transform.
            var skinning = bone.Offset * node.World;
            foreach (var (vertex, weight) in bone.Weights)
            {
                if (weight > 0f && vertex >= 0 && vertex < result.Length)
                {
                    result[vertex].Add((index, weight, skinning));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// The space the bones' bind matrices expect the mesh's vertices in. Assimp assumes the mesh's
    /// own vertex space, which is right for most writers, but Blender writes bind matrices that leave
    /// out the mesh object's transform (and with it the 100× scale of its default export). The right
    /// choice is the one that keeps the skinned mesh where the mesh's node puts it: posing moves arms
    /// and legs, but never shrinks a body a hundredfold or lays it on its back.
    /// </summary>
    private static Matrix4x4 ChooseBindSpace(
        Vector3[] positions, List<(int Bone, float Weight, Matrix4x4 Skinning)>[] influences, SceneNode node, SceneNode root)
    {
        var target = BoundingBox.FromPoints([.. positions.Select(p => Vector3.Transform(p, node.World))]);
        var tolerance = 1e-3f * target.Size.Length();
        var local = node.Parent is not null && Matrix4x4.Invert(node.Parent.World, out var parentInverse)
            ? node.World * parentInverse
            : node.World;
        var belowRoot = Matrix4x4.Invert(root.World, out var rootInverse) ? node.World * rootInverse : node.World;

        var best = Matrix4x4.Identity;
        var bestScore = float.MaxValue;
        foreach (var candidate in new[] { Matrix4x4.Identity, local, belowRoot })
        {
            var skinned = new Vector3[positions.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                skinned[i] = influences[i].Count > 0
                    ? Vector3.Transform(positions[i], candidate * Blend(influences[i]))
                    : Vector3.Transform(positions[i], node.World);
            }
            var bounds = BoundingBox.FromPoints(skinned);
            var score = Vector3.Distance(bounds.Min, target.Min) + Vector3.Distance(bounds.Max, target.Max);
            if (score < bestScore - tolerance)
            {
                (best, bestScore) = (candidate, score);
            }
        }
        return best;
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
}
