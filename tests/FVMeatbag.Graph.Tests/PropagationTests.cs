namespace FVMeatbag.Graph.Tests;

public class PropagationTests
{
    [Fact]
    public void Input_change_recalculates_the_whole_chain()
    {
        var graph = Graph.CreateRoot();
        var add = graph.AddNode(new AddOperation(), initialInput: new BinaryInput { A = 1, B = 2 });
        var mul = graph.AddNode(new MultiplyOperation(), initialInput: new BinaryInput { B = 10 });
        graph.Connect(add.Output("Value"), mul.Input("A"));

        Assert.Equal(30f, mul.OutputValues.Value);

        add.UpdateInputs((ref BinaryInput i) => i.A = 5);

        Assert.Equal(70f, mul.OutputValues.Value);
        Assert.Equal(NodeState.Clean, mul.State);
    }

    [Fact]
    public void Diamond_dependencies_evaluate_each_node_once_with_consistent_inputs()
    {
        var graph = Graph.CreateRoot();
        var source = graph.AddNode(new PassThroughOperation());
        var left = graph.AddNode(new PassThroughOperation(), "Left");
        var right = graph.AddNode(new PassThroughOperation(), "Right");
        var joinOp = new AddOperation();
        var join = graph.AddNode(joinOp);
        graph.Connect(source.Output("Value"), left.Input("Value"));
        graph.Connect(source.Output("Value"), right.Input("Value"));
        graph.Connect(left.Output("Value"), join.Input("A"));
        graph.Connect(right.Output("Value"), join.Input("B"));
        var before = joinOp.Executions;

        source.SetInputs(new ValueInput { Value = 4 });

        Assert.Equal(1, joinOp.Executions - before);
        Assert.Equal(8f, join.OutputValues.Value);
    }

    [Fact]
    public void Unchanged_output_stops_propagation()
    {
        var graph = Graph.CreateRoot();
        var sign = graph.AddNode(new SignOperation(), initialInput: new ValueInput { Value = 1 });
        var downstreamOp = new PassThroughOperation();
        var downstream = graph.AddNode(downstreamOp);
        graph.Connect(sign.Output("Value"), downstream.Input("Value"));
        var before = downstreamOp.Executions;

        sign.SetInputs(new ValueInput { Value = 42 });

        Assert.Equal(before, downstreamOp.Executions);
    }

    [Fact]
    public void Setting_an_identical_input_does_not_evaluate()
    {
        var graph = Graph.CreateRoot();
        var op = new PassThroughOperation();
        var node = graph.AddNode(op, initialInput: new ValueInput { Value = 3 });
        var before = op.Executions;

        node.SetInputs(new ValueInput { Value = 3 });
        node.Input("Value").SetValue(3f);

        Assert.Equal(before, op.Executions);
    }

    [Fact]
    public void Batch_coalesces_many_edits_into_one_evaluation()
    {
        var graph = Graph.CreateRoot();
        var add = graph.AddNode(new AddOperation());
        var downstreamOp = new PassThroughOperation();
        var downstream = graph.AddNode(downstreamOp);
        graph.Connect(add.Output("Value"), downstream.Input("Value"));
        var before = downstreamOp.Executions;

        using (graph.Evaluator.BeginBatch())
        {
            for (var i = 1; i <= 10; i++)
            {
                add.Input("A").SetValue((float)i);
            }
            Assert.True(graph.Evaluator.HasPendingWork);
        }

        Assert.Equal(1, downstreamOp.Executions - before);
        Assert.Equal(10f, downstream.OutputValues.Value);
    }

    [Fact]
    public void Deferred_mode_waits_for_an_explicit_evaluate()
    {
        var graph = Graph.CreateRoot(mode: EvaluationMode.Deferred);
        var add = graph.AddNode(new AddOperation());
        add.SetInputs(new BinaryInput { A = 1, B = 1 });

        Assert.Equal(NodeState.Dirty, add.State);
        Assert.Equal(1, graph.Evaluator.Evaluate());
        Assert.Equal(2f, add.OutputValues.Value);
        Assert.Equal(0, graph.Evaluator.Evaluate());
    }

    [Fact]
    public void Faulted_node_keeps_its_output_and_blocks_downstream_until_fixed()
    {
        var graph = Graph.CreateRoot();
        var faults = new List<Node>();
        graph.Evaluator.NodeFaulted += (_, e) => faults.Add(e.Node);
        var guard = graph.AddNode(new ThrowWhenNegativeOperation(), initialInput: new ValueInput { Value = 1 });
        var downstreamOp = new PassThroughOperation();
        var downstream = graph.AddNode(downstreamOp);
        graph.Connect(guard.Output("Value"), downstream.Input("Value"));
        var before = downstreamOp.Executions;

        guard.SetInputs(new ValueInput { Value = -1 });

        Assert.Equal(NodeState.Faulted, guard.State);
        Assert.IsType<InvalidOperationException>(guard.Fault);
        Assert.Equal([guard], faults);
        Assert.Equal(1f, guard.OutputValues.Value);
        Assert.Equal(before, downstreamOp.Executions);

        guard.SetInputs(new ValueInput { Value = 7 });

        Assert.Equal(NodeState.Clean, guard.State);
        Assert.Null(guard.Fault);
        Assert.Equal(7f, downstream.OutputValues.Value);
    }

    [Fact]
    public void OutputsChanged_reports_only_changed_ports()
    {
        var graph = Graph.CreateRoot();
        var add = graph.AddNode(new AddOperation());
        var changes = new List<string>();
        add.OutputsChanged += (_, e) => changes.AddRange(e.ChangedPorts.Select(p => p.Name));

        add.SetInputs(new BinaryInput { A = 1 });
        add.SetInputs(new BinaryInput { B = 1 }); // sum is still 1

        Assert.Equal(["Value"], changes);
    }

    [Fact]
    public void Operation_side_effects_on_other_nodes_run_in_the_same_pass()
    {
        var graph = Graph.CreateRoot(mode: EvaluationMode.Deferred);
        var target = graph.AddNode(new PassThroughOperation(), "Target");
        var trigger = graph.AddNode(new SetOtherNodeOperation(target));
        graph.Evaluator.Evaluate();

        trigger.SetInputs(new ValueInput { Value = 9 });
        graph.Evaluator.Evaluate();

        Assert.Equal(9f, target.OutputValues.Value);
        Assert.False(graph.Evaluator.HasPendingWork);
    }

    private sealed class SetOtherNodeOperation(Node<ValueInput, ValueOutput> target) : Operation<ValueInput, ValueOutput>
    {
        public override void Execute(in ValueInput input, ref ValueOutput output, EvaluationContext context) =>
            target.SetInputs(input);
    }
}
