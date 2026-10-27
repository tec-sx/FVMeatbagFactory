using FVMeatbag.Core.Project;
using FVMeatbag.Graph;

namespace FVMeatbag.Scene.Tests;

public class NewCharacterWorkflowTests
{
    [Fact]
    public void Load_create_and_add_run_in_order_and_link_scene_to_project()
    {
        var root = Graph.Graph.CreateRoot(mode: EvaluationMode.Deferred);
        var scene = new SceneRoot(root);
        var workflow = new NewCharacterWorkflow(root.CreateChild("Workflows"), scene);
        var order = new List<string>();
        root.Evaluator.NodeEvaluated += (_, e) => { if (e.Node.IsAction) { order.Add(e.Node.Name); } };

        workflow.Run("C:/meshes/BaseBody.fbx", "Hero");
        root.Evaluator.Evaluate();

        Assert.Equal(["LoadMesh", "CreateProject", "AddToScene"], order);
        var project = workflow.CreateProject.OutputValues.Project.Value;
        var component = workflow.AddToScene.OutputValues.Component.Value;
        Assert.Equal("Hero", project.Name);
        Assert.Same(scene.Root, component.Parent);
        Assert.NotNull(component.Graph);
        Assert.Equal(workflow.CreateProject.OutputValues.BaseMesh.Map(m => (ProjectItem)m), project.Find(component.AssetId));
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
        Assert.Empty(scene.Root.Children);
    }
}
