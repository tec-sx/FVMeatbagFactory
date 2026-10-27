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

internal static class TestSkinnedMeshes
{
    /// <summary>
    /// Writes a .glb with a two-bone vertical "arm" (a thin box from y=0 to y=2) bound in a straight
    /// pose. In the file's rest pose the armature is moved to <paramref name="armatureOffset"/> and
    /// the upper bone is bent 90° around Z, so the importer must deform the mesh to match.
    /// </summary>
    public static string WriteBentArm(string path, Vector3 armatureOffset)
    {
        var mesh = new MeshBuilder<VertexPositionNormal, VertexEmpty, VertexJoints4>("Arm");
        var primitive = mesh.UsePrimitive(MaterialBuilder.CreateDefault());
        // Lower half weighted to bone 0, upper half to bone 1, as quads around the Y axis.
        for (var segment = 0; segment < 2; segment++)
        {
            float y0 = segment, y1 = segment + 1;
            var corners = new[] { new Vector3(-0.1f, 0, -0.1f), new Vector3(0.1f, 0, -0.1f), new Vector3(0.1f, 0, 0.1f), new Vector3(-0.1f, 0, 0.1f) };
            for (var side = 0; side < 4; side++)
            {
                var a = corners[side];
                var b = corners[(side + 1) % 4];
                var normal = Vector3.Normalize(new Vector3(a.X + b.X, 0, a.Z + b.Z));
                VertexBuilder<VertexPositionNormal, VertexEmpty, VertexJoints4> V(Vector3 p) =>
                    new(new VertexPositionNormal(p, normal), default, new VertexJoints4((segment, 1f)));
                primitive.AddQuadrangle(V(a + (Vector3.UnitY * y0)), V(b + (Vector3.UnitY * y0)), V(b + (Vector3.UnitY * y1)), V(a + (Vector3.UnitY * y1)));
            }
        }

        var armature = new NodeBuilder("Armature").WithLocalTranslation(armatureOffset);
        var lower = armature.CreateNode("Lower");
        var upper = lower.CreateNode("Upper").WithLocalTranslation(Vector3.UnitY);
        upper.WithLocalRotation(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2));

        // Bind pose: straight arm at the origin, so the inverse bind matrices undo the bones' bind positions only.
        var scene = new SceneBuilder();
        scene.AddSkinnedMesh(mesh, [(lower, Matrix4x4.Identity), (upper, Matrix4x4.CreateTranslation(-Vector3.UnitY))]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        scene.ToGltf2().SaveGLB(path);
        return path;
    }
}
