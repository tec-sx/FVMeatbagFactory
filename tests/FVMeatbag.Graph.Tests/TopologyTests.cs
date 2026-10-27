namespace FVMeatbag.Graph.Tests;

public class TopologyTests
{
    [Fact]
    public void Connections_that_create_a_cycle_are_rejected()
    {
        var graph = Graph.CreateRoot();
        var a = graph.AddNode(new PassThroughOperation(), "A");
        var b = graph.AddNode(new PassThroughOperation(), "B");
        var c = graph.AddNode(new PassThroughOperation(), "C");
        graph.Connect(a.Output("Value"), b.Input("Value"));
        graph.Connect(b.Output("Value"), c.Input("Value"));

        Assert.Throws<GraphCycleException>(() => graph.Connect(c.Output("Value"), a.Input("Value")));
        Assert.Throws<GraphCycleException>(() => graph.Connect(a.Output("Value"), a.Input("Value")));
        Assert.False(a.Input("Value").IsConnected);
    }

    [Fact]
    public void Ports_of_different_types_cannot_be_connected()
    {
        var graph = Graph.CreateRoot();
        var number = graph.AddNode(new PassThroughOperation());
        var label = graph.AddNode(new LabelOperation());

        Assert.Throws<GraphException>(() => graph.Connect(number.Output("Value"), label.Input("Label")));
        Assert.Empty(number.Output("Value").Connections);
    }

    [Fact]
    public void Connecting_an_input_replaces_its_previous_connection()
    {
        var graph = Graph.CreateRoot();
        var first = graph.AddNode(new PassThroughOperation(), initialInput: new ValueInput { Value = 1 });
        var second = graph.AddNode(new PassThroughOperation(), initialInput: new ValueInput { Value = 2 });
        var sink = graph.AddNode(new PassThroughOperation());

        graph.Connect(first.Output("Value"), sink.Input("Value"));
        var edge = graph.Connect(second.Output("Value"), sink.Input("Value"));

        Assert.Empty(first.Output("Value").Connections);
        Assert.Same(edge, sink.Input("Value").Connection);
        Assert.Equal(2f, sink.OutputValues.Value);
    }

    [Fact]
    public void Ranks_follow_connections_made_in_any_order()
    {
        var graph = Graph.CreateRoot(mode: EvaluationMode.Deferred);
        var order = new List<string>();
        graph.Evaluator.NodeEvaluated += (_, e) => order.Add(e.Node.Name);
        var c = graph.AddNode(new PassThroughOperation(), "C");
        var b = graph.AddNode(new PassThroughOperation(), "B");
        var a = graph.AddNode(new PassThroughOperation(), "A");
        graph.Connect(b.Output("Value"), c.Input("Value"));
        graph.Connect(a.Output("Value"), b.Input("Value"));

        graph.Evaluator.Evaluate();

        Assert.Equal(["A", "B", "C"], order);
    }

    [Fact]
    public void Removing_a_node_disconnects_it()
    {
        var graph = Graph.CreateRoot();
        var a = graph.AddNode(new PassThroughOperation());
        var b = graph.AddNode(new PassThroughOperation());
        graph.Connect(a.Output("Value"), b.Input("Value"));

        graph.Remove(a);

        Assert.Null(a.Graph);
        Assert.False(b.Input("Value").IsConnected);
        Assert.DoesNotContain(a, graph.Nodes);
    }
}
