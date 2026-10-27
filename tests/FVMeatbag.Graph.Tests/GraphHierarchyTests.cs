namespace FVMeatbag.Graph.Tests;

public class GraphHierarchyTests
{
    [Fact]
    public void Child_graphs_share_the_root_evaluator()
    {
        var root = Graph.CreateRoot();
        var scene = root.CreateChild("Scene");
        var gizmo = scene.CreateChild("Gizmo");

        Assert.Same(root.Evaluator, gizmo.Evaluator);
        Assert.Same(root, gizmo.Root);
        Assert.Equal("Root/Scene/Gizmo", gizmo.ToString());
        Assert.Equal([root, scene, gizmo], root.DescendantsAndSelf());
    }

    [Fact]
    public void Nodes_in_different_child_graphs_can_be_connected()
    {
        var root = Graph.CreateRoot();
        var project = root.CreateChild("Project");
        var scene = root.CreateChild("Scene");
        var source = project.AddNode(new PassThroughOperation());
        var view = scene.AddNode(new PassThroughOperation());

        scene.Connect(source.Output("Value"), view.Input("Value"));
        source.SetInputs(new ValueInput { Value = 3 });

        Assert.Equal(3f, view.OutputValues.Value);
    }

    [Fact]
    public void Removing_a_child_graph_tears_down_its_nodes_and_connections()
    {
        var root = Graph.CreateRoot();
        var source = root.AddNode(new PassThroughOperation());
        var component = root.CreateChild("MeshComponent");
        var behaviour = component.CreateChild("Behaviour");
        var reader = behaviour.AddNode(new PassThroughOperation());
        root.Connect(source.Output("Value"), reader.Input("Value"));

        root.RemoveChild(component);

        Assert.Empty(root.Children);
        Assert.False(component.IsAttached);
        Assert.False(behaviour.IsAttached);
        Assert.Null(reader.Graph);
        Assert.Empty(source.Output("Value").Connections);
        Assert.Throws<GraphException>(() => behaviour.AddNode(new PassThroughOperation()));
    }

    [Fact]
    public void Nodes_from_separate_roots_cannot_be_connected()
    {
        var first = Graph.CreateRoot("First");
        var second = Graph.CreateRoot("Second");
        var a = first.AddNode(new PassThroughOperation());
        var b = second.AddNode(new PassThroughOperation());

        Assert.Throws<GraphException>(() => first.Connect(a.Output("Value"), b.Input("Value")));
    }
}
