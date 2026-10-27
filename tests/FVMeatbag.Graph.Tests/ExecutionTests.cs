namespace FVMeatbag.Graph.Tests;

public class ExecutionTests
{
    [Fact]
    public void Actions_ignore_data_changes_until_triggered()
    {
        var graph = Graph.CreateRoot();
        var log = new List<string>();
        var source = graph.AddNode(new PassThroughOperation());
        var action = graph.AddNode(new LogAction("Act", log));
        graph.Connect(source.Output("Value"), action.Input("Value"));

        source.SetInputs(new ValueInput { Value = 5 });
        Assert.Empty(log);

        action.Trigger();
        Assert.Equal(["Act:5"], log);
    }

    [Fact]
    public void Chain_runs_in_order_and_each_step_sees_earlier_results()
    {
        var graph = Graph.CreateRoot();
        var log = new List<string>();
        var first = graph.AddNode(new LogAction("First", log), initialInput: new ValueInput { Value = 1 });
        var doubler = graph.AddNode(new MultiplyOperation(), initialInput: new BinaryInput { B = 2 });
        var second = graph.AddNode(new LogAction("Second", log));
        graph.Connect(first.Output("Value"), doubler.Input("A"));
        graph.Connect(doubler.Output("Value"), second.Input("Value"));
        graph.Connect(first.ExecOut(), second.ExecIn());

        first.UpdateInputs((ref ValueInput i) => i.Value = 3);
        first.Trigger();

        Assert.Equal(["First:3", "Second:6"], log);
        Assert.Equal(6f, second.OutputValues.Value);
    }

    [Fact]
    public void Branch_continues_only_through_the_fired_output()
    {
        var graph = Graph.CreateRoot();
        var log = new List<string>();
        var branch = graph.AddNode(new BranchAction());
        var positive = graph.AddNode(new LogAction("Positive", log));
        var negative = graph.AddNode(new LogAction("Negative", log));
        graph.Connect(branch.ExecOut("True"), positive.ExecIn());
        graph.Connect(branch.ExecOut("False"), negative.ExecIn());

        branch.SetInputs(new ValueInput { Value = -1 });
        branch.Trigger();
        branch.SetInputs(new ValueInput { Value = 1 });
        branch.Trigger();

        Assert.Equal(["Negative:0", "Positive:0"], log);
    }

    [Fact]
    public void Sequence_runs_each_branch_to_completion_before_the_next()
    {
        var graph = Graph.CreateRoot();
        var log = new List<string>();
        var sequence = graph.AddNode(new SequenceAction());
        var a = graph.AddNode(new LogAction("A", log));
        var a2 = graph.AddNode(new LogAction("A2", log));
        var b = graph.AddNode(new LogAction("B", log));
        graph.Connect(sequence.ExecOut("Then 0"), a.ExecIn());
        graph.Connect(a.ExecOut(), a2.ExecIn());
        graph.Connect(sequence.ExecOut("Then 1"), b.ExecIn());

        sequence.Trigger();

        Assert.Equal(["A:0", "A2:0", "B:0"], log);
    }

    [Fact]
    public void Faulted_action_stops_the_chain()
    {
        var graph = Graph.CreateRoot();
        var log = new List<string>();
        var guard = graph.AddNode(new ThrowingAction());
        var next = graph.AddNode(new LogAction("Next", log));
        graph.Connect(guard.ExecOut(), next.ExecIn());

        guard.Trigger();

        Assert.Equal(NodeState.Faulted, guard.State);
        Assert.Empty(log);
    }

    [Fact]
    public void Deferred_triggers_run_on_evaluate_after_data_settles()
    {
        var graph = Graph.CreateRoot(mode: EvaluationMode.Deferred);
        var log = new List<string>();
        var source = graph.AddNode(new PassThroughOperation());
        var action = graph.AddNode(new LogAction("Act", log));
        graph.Connect(source.Output("Value"), action.Input("Value"));

        action.Trigger();
        source.SetInputs(new ValueInput { Value = 8 });
        Assert.Empty(log);
        Assert.True(graph.Evaluator.HasPendingWork);

        graph.Evaluator.Evaluate();
        Assert.Equal(["Act:8"], log);
    }

    [Fact]
    public void Runaway_exec_loops_are_stopped()
    {
        var graph = Graph.CreateRoot(mode: EvaluationMode.Deferred);
        graph.Evaluator.MaxEvaluationsPerPass = 50;
        var log = new List<string>();
        var a = graph.AddNode(new LogAction("A", log));
        var b = graph.AddNode(new LogAction("B", log));
        graph.Connect(a.ExecOut(), b.ExecIn());
        graph.Connect(b.ExecOut(), a.ExecIn());

        a.Trigger();

        Assert.Throws<GraphException>(() => graph.Evaluator.Evaluate());
    }

    [Fact]
    public void Exec_output_rewires_and_removal_disconnects()
    {
        var graph = Graph.CreateRoot();
        var log = new List<string>();
        var start = graph.AddNode(new LogAction("Start", log));
        var first = graph.AddNode(new LogAction("First", log));
        var second = graph.AddNode(new LogAction("Second", log));

        graph.Connect(start.ExecOut(), first.ExecIn());
        graph.Connect(start.ExecOut(), second.ExecIn());
        Assert.Empty(first.ExecIn().Connections);

        graph.Remove(second);
        Assert.Null(start.ExecOut().Connection);

        start.Trigger();
        Assert.Equal(["Start:0"], log);
    }

    [Fact]
    public void Data_nodes_have_no_exec_pins_and_cannot_fire()
    {
        var graph = Graph.CreateRoot();
        var node = graph.AddNode(new FireFromDataOperation());

        Assert.False(node.IsAction);
        Assert.Empty(node.ExecInputs);
        Assert.Equal(NodeState.Faulted, node.State);
        Assert.IsType<GraphException>(node.Fault);
    }

    private sealed class LogAction(string name, List<string> log) : ActionOperation<ValueInput, ValueOutput>
    {
        public override void Execute(in ValueInput input, ref ValueOutput output, EvaluationContext context)
        {
            log.Add($"{name}:{input.Value}");
            output.Value = input.Value;
        }
    }

    private sealed class BranchAction : ActionOperation<ValueInput, Unit>
    {
        public override IReadOnlyList<string> ExecOutputs => ["True", "False"];

        public override void Execute(in ValueInput input, ref Unit output, EvaluationContext context) =>
            context.Fire(input.Value >= 0 ? "True" : "False");
    }

    private sealed class SequenceAction : ActionOperation<Unit, Unit>
    {
        public override IReadOnlyList<string> ExecOutputs => ["Then 0", "Then 1"];

        public override void Execute(in Unit input, ref Unit output, EvaluationContext context)
        {
            context.Fire("Then 0");
            context.Fire("Then 1");
        }
    }

    private sealed class ThrowingAction : ActionOperation<Unit, Unit>
    {
        public override void Execute(in Unit input, ref Unit output, EvaluationContext context) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class FireFromDataOperation : Operation<Unit, Unit>
    {
        public override void Execute(in Unit input, ref Unit output, EvaluationContext context) =>
            context.Fire("Then");
    }
}
