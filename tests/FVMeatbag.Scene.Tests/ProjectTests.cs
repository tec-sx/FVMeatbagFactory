using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Project;

namespace FVMeatbag.Scene.Tests;

public sealed class ProjectTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void First_run_creates_the_projects_folder_with_a_demo_project()
    {
        var workspace = new ProjectWorkspace(_temp.Combine("Documents", "Projects"));

        var demo = workspace.EnsureCreated();

        Assert.Equal(_temp.Combine("Documents", "Projects", "Demo"), demo);
        var project = ProjectFile.Load(demo);
        Assert.Equal("Demo", project.Name);
        Assert.Equal(OrbitCameraSettings.Default, project.Camera);
        Assert.Equal(DirectionalLightSettings.Default, project.Light);
        Assert.Empty(project.MeshAssets);
    }

    [Fact]
    public void Later_runs_keep_the_existing_demo_project()
    {
        var workspace = new ProjectWorkspace(_temp.Combine("Projects"));
        var demo = ProjectFile.Load(workspace.EnsureCreated());
        demo.Camera = demo.Camera with { YawDegrees = 90 };
        ProjectFile.Save(demo);

        workspace.EnsureCreated();

        Assert.Equal(90, ProjectFile.Load(workspace.DemoDirectory).Camera.YawDegrees);
    }

    [Fact]
    public void New_project_folders_never_reuse_an_existing_one()
    {
        var workspace = new ProjectWorkspace(_temp.Combine("Projects"));
        workspace.EnsureCreated();

        Assert.Equal(_temp.Combine("Projects", "Demo 2"), workspace.NewProjectDirectory("Demo"));
        Assert.Equal(_temp.Combine("Projects", "a_b"), workspace.NewProjectDirectory("a/b"));
    }

    [Fact]
    public void Save_and_load_round_trip_view_settings_and_meshes()
    {
        var directory = _temp.Combine("Projects", "Hero");
        var project = new CharacterProject("Hero", directory)
        {
            Camera = new OrbitCameraSettings(new Vector3(1, 2, 3), 45, -20, 7, 50),
            Light = new DirectionalLightSettings(-60, 30, new Vector3(1, 0.9f, 0.8f), 3),
        };
        var path = ProjectFile.CopyIntoProject(project, TestMeshes.WriteCube(_temp.Combine("Head.glb")));
        project.Meshes.Add(new SkeletalMeshAsset("Head", path));
        ProjectFile.Save(project);

        var loaded = ProjectFile.Load(directory);

        Assert.Equal(project.Camera, loaded.Camera);
        Assert.Equal(project.Light, loaded.Light);
        var mesh = Assert.Single(loaded.MeshAssets);
        Assert.Equal("Meshes/Head.glb", mesh.SourcePath);
        Assert.Equal(24, mesh.Geometry.Value.VertexCount);
    }

    [Fact]
    public void A_missing_mesh_file_is_reported_and_the_project_still_opens()
    {
        var directory = _temp.Combine("Projects", "Hero");
        var project = new CharacterProject("Hero", directory);
        project.Meshes.Add(new SkeletalMeshAsset("Gone", "Meshes/Gone.glb"));
        ProjectFile.Save(project);
        var problems = new List<string>();

        var loaded = ProjectFile.Load(directory, problems);

        Assert.True(Assert.Single(loaded.MeshAssets).Geometry.IsNone);
        Assert.Contains("Gone.glb", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void Gltf_import_bakes_node_transforms_and_keeps_normals()
    {
        var glb = TestMeshes.WriteCube(_temp.Combine("Cube.glb"), size: 2f, offset: new Vector3(0, 3, 0));

        var mesh = GltfImporter.Load(glb);

        Assert.Equal("Cube", mesh.Name);
        Assert.Equal(12, mesh.Geometry.TriangleCount);
        Assert.Equal(new BoundingBox(new Vector3(-1, 2, -1), new Vector3(1, 4, 1)), mesh.Geometry.Bounds);
        Assert.All(mesh.Geometry.Normals.ToArray(), n => Assert.Equal(1f, n.Length(), 1e-5f));
    }
}
