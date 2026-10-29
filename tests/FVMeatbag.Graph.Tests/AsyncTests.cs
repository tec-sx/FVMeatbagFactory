namespace FVMeatbag.Graph.Tests;

public class AsyncTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Result_lands_in_a_later_pass_and_then_propagates()
    {
        var graph = Graph.CreateRoot();
        var gate = new Gate();
        var square = graph.AddNode(new GatedSquareOperation(gate));
        var downstream = graph.AddNode(new PassThroughOperation());
        graph.Connect(square.Output("Value"), downstream.Input("Value"));

        square.SetInputs(new ValueInput { Value = 3 });
        Assert.Equal(NodeState.Running, square.State);
        Assert.Equal(0f, square.OutputValues.Value);
        Assert.Equal(0f, downstream.OutputValues.Value);

        gate.Open();
        await Settle(graph);

        Assert.Equal(NodeState.Clean, square.State);
        Assert.Equal(9f, square.OutputValues.Value);
        Assert.Equal(9f, downstream.OutputValues.Value);
        Assert.True(square.Progress.IsNone);
    }

    [Fact]
    public async Task Input_change_while_running_cancels_and_restarts_with_the_new_input()
    {
        var graph = Graph.CreateRoot();
        var gate = new Gate();
        var operation = new GatedSquareOperation(gate);
        var square = graph.AddNode(operation);

        square.SetInputs(new ValueInput { Value = 2 });
        await gate.Reached.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        square.SetInputs(new ValueInput { Value = 5 });
        gate.Open();
        await Settle(graph);

        Assert.Equal(25f, square.OutputValues.Value);
        Assert.Equal(NodeState.Clean, square.State);
        Assert.Contains(2f, operation.Cancelled);
    }

    [Fact]
    public async Task Throwing_faults_the_node_and_keeps_the_previous_output()
    {
        var graph = Graph.CreateRoot();
        var square = graph.AddNode(new GatedSquareOperation(Gate.AlreadyOpen()));
        var faults = new List<Exception>();
        graph.Evaluator.NodeFaulted += (_, e) => faults.Add(e.Exception);

        square.SetInputs(new ValueInput { Value = 4 });
        await Settle(graph);
        square.SetInputs(new ValueInput { Value = -1 });
        await Settle(graph);

        Assert.Equal(NodeState.Faulted, square.State);
        Assert.IsType<InvalidOperationException>(square.Fault);
        Assert.Equal(16f, square.OutputValues.Value);
        Assert.IsType<InvalidOperationException>(Assert.Single(faults));
    }

    [Fact]
    public async Task Removing_the_node_cancels_its_work_and_drops_the_result()
    {
        var graph = Graph.CreateRoot();
        var gate = new Gate();
        var operation = new GatedSquareOperation(gate);
        var square = graph.AddNode(operation, initialInput: new ValueInput { Value = 7 });
        Assert.Equal(NodeState.Running, square.State);
        await gate.Reached.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        graph.Remove(square);
        await Settle(graph);

        Assert.Equal([7f], operation.Cancelled);
        Assert.Equal(0f, square.OutputValues.Value);
        Assert.Equal(0, graph.Evaluator.RunningCount);
    }

    [Fact]
    public async Task Cancel_faults_the_node_with_a_cancellation()
    {
        var graph = Graph.CreateRoot();
        var square = graph.AddNode(new GatedSquareOperation(new Gate()), initialInput: new ValueInput { Value = 7 });

        square.Cancel();
        await Settle(graph);

        Assert.Equal(NodeState.Faulted, square.State);
        Assert.IsAssignableFrom<OperationCanceledException>(square.Fault);
    }

    [Fact]
    public async Task Progress_is_visible_while_running()
    {
        var graph = Graph.CreateRoot();
        var gate = new Gate();
        var square = graph.AddNode(new GatedSquareOperation(gate), initialInput: new ValueInput { Value = 1 });

        await gate.Reached.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(0.5f, square.Progress.Value);
        gate.Open();
        await Settle(graph);
        Assert.True(square.Progress.IsNone);
    }

    [Fact]
    public async Task Completion_is_announced_and_applied_only_by_evaluate()
    {
        var graph = Graph.CreateRoot();
        var announced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        graph.Evaluator.AsyncWorkCompleted += (_, _) => announced.TrySetResult();
        var square = graph.AddNode(new GatedSquareOperation(Gate.AlreadyOpen()), initialInput: new ValueInput { Value = 3 });

        await announced.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await graph.Evaluator.WhenAsyncIdle().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.True(graph.Evaluator.HasPendingWork);
        Assert.Equal(NodeState.Running, square.State);

        graph.Evaluator.Evaluate();
        Assert.Equal(9f, square.OutputValues.Value);
        Assert.False(graph.Evaluator.HasPendingWork);
    }

    [Fact]
    public async Task Async_action_pauses_the_chain_and_resumes_it_when_done()
    {
        var graph = Graph.CreateRoot();
        var log = new List<string>();
        var gate = new Gate();
        var bake = graph.AddNode(new GatedBakeAction(gate, log), initialInput: new ValueInput { Value = 2 });
        var export = graph.AddNode(new LogAction("Export", log));
        graph.Connect(bake.Output("Value"), export.Input("Value"));
        graph.Connect(bake.ExecOut(), export.ExecIn());

        bake.Trigger();
        await gate.Reached.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(["Bake:2"], log);
        Assert.Equal(NodeState.Running, bake.State);

        gate.Open();
        await Settle(graph);
        Assert.Equal(["Bake:2", "Export:20"], log);
    }

    [Fact]
    public async Task Failed_async_action_stops_the_chain()
    {
        var graph = Graph.CreateRoot();
        var log = new List<string>();
        var bake = graph.AddNode(new GatedBakeAction(Gate.AlreadyOpen(), log), initialInput: new ValueInput { Value = -2 });
        var export = graph.AddNode(new LogAction("Export", log));
        graph.Connect(bake.ExecOut(), export.ExecIn());

        bake.Trigger();
        await Settle(graph);

        Assert.Equal(["Bake:-2"], log);
        Assert.Equal(NodeState.Faulted, bake.State);
    }

    [Fact]
    public void Async_operations_cannot_be_executed_inline()
    {
        Operation<ValueInput, ValueOutput> operation = new GatedSquareOperation(Gate.AlreadyOpen());
        var output = default(ValueOutput);

        Assert.Throws<GraphException>(() => operation.Execute(default, ref output, null!));
    }

    private static Task Settle(Graph graph) =>
        graph.Evaluator.SettleAsync(TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);

    /// <summary>Lets a test decide when async work may finish.</summary>
    private sealed class Gate
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Gate AlreadyOpen()
        {
            var gate = new Gate();
            gate.Open();
            return gate;
        }

        /// <summary>Completes once work reached the gate.</summary>
        public Task Reached => _reached.Task;

        public void Open() => _open.TrySetResult();

        public async Task PassAsync(CancellationToken cancellation)
        {
            _reached.TrySetResult();
            await _open.Task.WaitAsync(cancellation).ConfigureAwait(false);
        }
    }

    /// <summary>Squares its input after the gate opens; negative inputs throw.</summary>
    private sealed class GatedSquareOperation(Gate gate) : AsyncOperation<ValueInput, ValueOutput>
    {
        private readonly Lock _lock = new();
        private readonly List<float> _cancelled = [];

        public IReadOnlyList<float> Cancelled
        {
            get
            {
                lock (_lock)
                {
                    return [.. _cancelled];
                }
            }
        }

        public override async Task<ValueOutput> RunAsync(ValueInput input, ValueOutput previous, AsyncContext context)
        {
            context.ReportProgress(0.5f);
            try
            {
                await gate.PassAsync(context.Cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                lock (_lock)
                {
                    _cancelled.Add(input.Value);
                }
                throw;
            }
            return input.Value < 0
                ? throw new InvalidOperationException("Negative input.")
                : new ValueOutput { Value = input.Value * input.Value };
        }
    }

    /// <summary>Logs when it starts, then multiplies by ten once the gate opens.</summary>
    private sealed class GatedBakeAction(Gate gate, List<string> log) : AsyncActionOperation<ValueInput, ValueOutput>
    {
        public override Task<ValueOutput> RunAsync(ValueInput input, ValueOutput previous, AsyncContext context)
        {
            lock (log)
            {
                log.Add($"Bake:{input.Value}");
            }
            return Bake(input, context);
        }

        private async Task<ValueOutput> Bake(ValueInput input, AsyncContext context)
        {
            await gate.PassAsync(context.Cancellation).ConfigureAwait(false);
            return input.Value < 0 ? throw new InvalidOperationException("Nothing to bake.") : new ValueOutput { Value = input.Value * 10 };
        }
    }

    private sealed class LogAction(string name, List<string> log) : ActionOperation<ValueInput, ValueOutput>
    {
        public override void Execute(in ValueInput input, ref ValueOutput output, EvaluationContext context)
        {
            lock (log)
            {
                log.Add($"{name}:{input.Value}");
            }
            output.Value = input.Value;
        }
    }
}
