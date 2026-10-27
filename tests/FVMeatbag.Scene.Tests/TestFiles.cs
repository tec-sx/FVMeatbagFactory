using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace FVMeatbag.Scene.Tests;

/// <summary>A scratch folder per test, deleted afterwards.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fvmeatbag-tests", Guid.NewGuid().ToString("N"));

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class TestMeshes
{
    /// <summary>Writes a .glb holding a cube of the given size whose node is moved to <paramref name="offset"/>.</summary>
    public static string WriteCube(string path, float size = 1f, Vector3 offset = default)
    {
        var mesh = new MeshBuilder<VertexPositionNormal>("Cube");
        var primitive = mesh.UsePrimitive(MaterialBuilder.CreateDefault());
        var h = size * 0.5f;
        void Face(Vector3 normal, Vector3 u, Vector3 v)
        {
            var c = normal * h;
            VertexPositionNormal V(Vector3 p) => new(p, normal);
            primitive.AddQuadrangle(V(c - (u * h) - (v * h)), V(c + (u * h) - (v * h)), V(c + (u * h) + (v * h)), V(c - (u * h) + (v * h)));
        }
        Face(Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY);
        Face(-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY);
        Face(Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ);
        Face(-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ);
        Face(Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);
        Face(-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY);

        var scene = new SceneBuilder();
        scene.AddRigidMesh(mesh, Matrix4x4.CreateTranslation(offset));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        scene.ToGltf2().SaveGLB(path);
        return path;
    }
}
