using System.Numerics;
using FVMeatbag.Core.Project;
using FVMeatbag.Graph;

namespace FVMeatbag.Scene.Tests;

public sealed class NewCharacterWorkflowTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Load_create_add_and_save_run_in_order_and_link_scene_to_project()
    {
        var root = Graph.Graph.CreateRoot(mode: EvaluationMode.Deferred);
        var scene = new SceneRoot(root);
        var workflow = new NewCharacterWorkflow(root.CreateChild("Workflows"), scene);
        var workspace = new ProjectWorkspace(_temp.Combine("Projects"));
        var order = new List<string>();
        root.Evaluator.NodeEvaluated += (_, e) => { if (e.Node.IsAction) { order.Add(e.Node.Name); } };
        var glb = TestMeshes.WriteCube(_temp.Combine("BaseBody.glb"));

        workflow.Run(glb, "Hero", workspace);
        root.Evaluator.Evaluate();

        Assert.Equal(["LoadMesh", "CreateProject", "AddMeshToProject", "AddToScene", "SaveProject"], order);
        var project = workflow.CreateProject.OutputValues.Project.Value;
        var component = workflow.AddToScene.OutputValues.Component.Value;
        Assert.Equal("Hero", project.Name);
        Assert.Equal(project, scene.Project);
        Assert.Same(scene.Root, component.Parent);
        Assert.NotNull(component.Graph);
        Assert.Equal(workflow.AddMesh.OutputValues.Asset.Map(m => (ProjectItem)m), project.Find(component.AssetId));
        Assert.True(File.Exists(_temp.Combine("Projects", "Hero", "Meshes", "BaseBody.glb")));
        Assert.True(ProjectFile.Exists(_temp.Combine("Projects", "Hero")));
    }

    [Fact]
    public void Failed_load_stops_before_a_project_is_created()
    {
        var root = Graph.Graph.CreateRoot();
        var scene = new SceneRoot(root);
        var workflow = new NewCharacterWorkflow(root, scene);

        workflow.LoadMesh.Trigger(); // no path set

        Assert.Equal(NodeState.Faulted, workflow.LoadMesh.State);
        Assert.True(workflow.CreateProject.OutputValues.Project.IsNone);
        Assert.Equal([scene.Camera, scene.Light], scene.Root.Children);
    }

    [Fact]
    public void Import_places_the_mesh_at_the_scene_center_and_frames_it()
    {
        var root = Graph.Graph.CreateRoot();
        var scene = new SceneRoot(root);
        var project = new CharacterProject("Demo", _temp.Combine("Demo"));
        scene.Show(project);
        var workflow = new ImportMeshWorkflow(root.CreateChild("Import"), scene);
        var glb = TestMeshes.WriteCube(_temp.Combine("Suzanne.glb"), size: 2f, offset: new Vector3(5, 1, -3));

        workflow.Run(glb, project);

        var component = Assert.Single(scene.Meshes);
        var asset = scene.FindAsset(component).Value;
        Assert.Equal("Suzanne", asset.Name);
        Assert.Equal("Meshes/Suzanne.glb", asset.SourcePath);
        AssertNear(new Vector3(5, 1, -3), asset.Geometry.Value.Bounds.Center);
        AssertNear(Vector3.Zero, Vector3.Transform(asset.Geometry.Value.Bounds.Center, component.WorldMatrix));
        AssertNear(Vector3.Zero, scene.Camera.Settings.Target);
        Assert.True(scene.Camera.Settings.Distance > asset.Geometry.Value.Bounds.Radius);
    }

    [Fact]
    public void Importing_a_non_gltf_file_faults_and_adds_nothing()
    {
        var root = Graph.Graph.CreateRoot();
        var scene = new SceneRoot(root);
        var project = new CharacterProject("Demo");
        scene.Show(project);
        var workflow = new ImportMeshWorkflow(root, scene);

        workflow.Run(_temp.Combine("Body.fbx"), project);

        Assert.Equal(NodeState.Faulted, workflow.LoadMesh.State);
        Assert.IsType<NotSupportedException>(workflow.LoadMesh.Fault);
        Assert.Empty(scene.Meshes);
        Assert.Empty(project.MeshAssets);
    }

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}.");
}
