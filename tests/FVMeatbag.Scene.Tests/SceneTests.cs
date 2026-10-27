using System.Numerics;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Components;

namespace FVMeatbag.Scene.Tests;

public class SceneTests
{
    private static readonly Transform MovedUp = Transform.Identity with { Position = new Vector3(0, 2, 0) };

    [Fact]
    public void Component_graphs_mirror_the_scene_hierarchy()
    {
        var root = Graph.Graph.CreateRoot();
        var scene = new SceneRoot(root);
        var body = scene.Root.AddChild(new MeshComponent("Body", Guid.NewGuid()));
        var head = body.AddChild(new SceneComponent("Head"));

        Assert.Same(scene.Graph, scene.Root.Graph!.Parent);
        Assert.Same(scene.Root.Graph, body.Graph!.Parent);
        Assert.Same(body.Graph, head.Graph!.Parent);
        Assert.Equal("Root/Scene/Root/Body/Head", head.Graph.ToString());
    }

    [Fact]
    public void Moving_a_parent_propagates_to_child_world_matrices()
    {
        var scene = new SceneRoot(Graph.Graph.CreateRoot());
        var body = scene.Root.AddChild(new SceneComponent("Body"));
        var head = body.AddChild(new SceneComponent("Head") { LocalTransform = MovedUp });

        body.LocalTransform = Transform.Identity with { Position = new Vector3(1, 0, 0) };

        Assert.Equal(new Vector3(1, 2, 0), head.WorldMatrix.Translation);
    }

    [Fact]
    public void Deferred_graph_updates_world_matrices_on_evaluate()
    {
        var root = Graph.Graph.CreateRoot(mode: EvaluationMode.Deferred);
        var scene = new SceneRoot(root);
        var body = scene.Root.AddChild(new SceneComponent("Body"));
        root.Evaluator.Evaluate();

        body.LocalTransform = MovedUp;
        Assert.Equal(Vector3.Zero, body.WorldMatrix.Translation);

        root.Evaluator.Evaluate();
        Assert.Equal(new Vector3(0, 2, 0), body.WorldMatrix.Translation);
    }

    [Fact]
    public void Selecting_adds_a_gizmo_that_follows_the_target()
    {
        var scene = new SceneRoot(Graph.Graph.CreateRoot());
        var body = scene.Root.AddChild(new SceneComponent("Body"));

        scene.Select(body);
        body.LocalTransform = MovedUp;

        Assert.NotNull(scene.Gizmo);
        Assert.Same(scene.Root, scene.Gizmo.Parent);
        Assert.Equal(new Vector3(0, 2, 0), scene.Gizmo.WorldMatrix.Translation);
    }

    [Fact]
    public void Changing_selection_tears_down_the_previous_gizmo_graph()
    {
        var scene = new SceneRoot(Graph.Graph.CreateRoot());
        var body = scene.Root.AddChild(new SceneComponent("Body"));
        var prop = scene.Root.AddChild(new SceneComponent("Prop"));
        scene.Select(body);
        var firstGizmoGraph = scene.Gizmo!.Graph!;

        scene.Select(prop);

        Assert.False(firstGizmoGraph.IsAttached);
        Assert.Same(prop, scene.Gizmo!.Target);
        Assert.Empty(body.TransformNode!.Output("World").Connections);
    }

    [Fact]
    public void Removing_a_component_detaches_its_subtree()
    {
        var scene = new SceneRoot(Graph.Graph.CreateRoot());
        var body = scene.Root.AddChild(new SceneComponent("Body"));
        var head = body.AddChild(new SceneComponent("Head"));
        var headGraph = head.Graph!;

        scene.Root.RemoveChild(body);

        Assert.Null(body.Scene);
        Assert.Null(head.Graph);
        Assert.False(headGraph.IsAttached);
        Assert.Equal([scene.Camera.Graph!, scene.Light.Graph!], scene.Root.Graph!.Children);
    }

    [Fact]
    public void Removing_the_selected_component_clears_the_selection()
    {
        var scene = new SceneRoot(Graph.Graph.CreateRoot());
        var body = scene.Root.AddChild(new SceneComponent("Body"));
        var head = body.AddChild(new SceneComponent("Head"));
        scene.Select(head);

        scene.Root.RemoveChild(body);

        Assert.Null(scene.Selected);
        Assert.Null(scene.Gizmo);
        Assert.Equal([scene.Camera, scene.Light], scene.Root.Children);
    }
}
