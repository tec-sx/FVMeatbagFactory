using FVMeatbag.Graph.Ports;

namespace FVMeatbag.Graph;

/// <summary>Mutates an input struct in place.</summary>
public delegate void InputUpdate<TIn>(ref TIn input) where TIn : struct;

/// <summary>A node whose ports are the public fields of <typeparamref name="TIn"/> and <typeparamref name="TOut"/>.</summary>
public class Node<TIn, TOut> : Node
    where TIn : struct
    where TOut : struct
{
    private static readonly IReadOnlyList<int> NoChanges = [];

    private TIn _input;
    private TOut _output;
    private AsyncJob? _job;

    public Node(Operation<TIn, TOut> operation, string? name = null, TIn initialInput = default)
        : base(name ?? operation.Name)
    {
        Operation = operation;
        _input = initialInput;
        Inputs = PortSchema<TIn>.Fields.Select(f => f.CreateInputPort(this)).ToArray();
        Outputs = PortSchema<TOut>.Fields.Select(f => f.CreateOutputPort(this)).ToArray();
        if (operation is IActionOperation action)
        {
            ExecInputs = action.ExecInputs.Select((n, i) => new ExecInput(this, n, i)).ToArray();
            ExecOutputs = action.ExecOutputs.Select((n, i) => new ExecOutput(this, n, i)).ToArray();
        }
        else
        {
            ExecInputs = [];
            ExecOutputs = [];
        }
    }

    public Operation<TIn, TOut> Operation { get; }
    public override IReadOnlyList<InputPort> Inputs { get; }
    public override IReadOnlyList<OutputPort> Outputs { get; }
    public override IReadOnlyList<ExecInput> ExecInputs { get; }
    public override IReadOnlyList<ExecOutput> ExecOutputs { get; }

    /// <summary>A copy of the current input values.</summary>
    public TIn InputValues => _input;

    /// <summary>A copy of the current output values.</summary>
    public TOut OutputValues => _output;

    public override Maybe<float> Progress => _job is { } job ? job.Context.Progress : Maybe<float>.None;

    internal ref TIn InputRef => ref _input;
    internal ref TOut OutputRef => ref _output;

    /// <summary>Replaces the input struct. The node is marked dirty only if a field changed.</summary>
    public void SetInputs(in TIn input)
    {
        var changed = AnyDiffers(PortSchema<TIn>.Fields, _input, input);
        _input = input;
        if (changed)
        {
            MarkDirty();
        }
    }

    /// <summary>Edits the input struct in place, e.g. <c>node.UpdateInputs((ref BrushInput i) => i.Radius = 2f)</c>.</summary>
    public void UpdateInputs(InputUpdate<TIn> update)
    {
        var next = _input;
        update(ref next);
        SetInputs(next);
    }

    internal override IReadOnlyList<int> Evaluate(EvaluationContext context)
    {
        foreach (var input in Inputs)
        {
            input.Connection?.Transfer();
        }

        if (Operation is AsyncOperation<TIn, TOut> async)
        {
            Start(async, context.Evaluator);
            return NoChanges;
        }

        var previous = _output;
        try
        {
            Operation.Execute(in _input, ref _output, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _output = previous;
            State = NodeState.Faulted;
            Fault = ex;
            return NoChanges;
        }

        State = NodeState.Clean;
        Fault = null;

        List<int>? changed = null;
        foreach (var field in PortSchema<TOut>.Fields)
        {
            if (field.Differs(previous, _output))
            {
                (changed ??= []).Add(field.Index);
            }
        }
        return changed ?? NoChanges;
    }

    public override void Cancel() => _job?.Cancellation.Cancel();

    internal override Maybe<AsyncCompletion> CompleteAsync(object job)
    {
        var finished = (AsyncJob)job;
        if (finished != _job || Graph is null)
        {
            finished.Cancellation.Dispose();
            return Maybe<AsyncCompletion>.None; // superseded by a newer run, or removed
        }
        _job = null;
        finished.Cancellation.Dispose();

        var task = finished.Task!;
        if (!task.IsCompletedSuccessfully)
        {
            State = NodeState.Faulted;
            Fault = task.IsCanceled
                ? new OperationCanceledException($"{Name} was cancelled.")
                : task.Exception!.InnerExceptions.Count == 1 ? task.Exception.InnerException! : task.Exception;
            return new AsyncCompletion(NoChanges, []);
        }

        var previous = _output;
        _output = task.Result;
        State = NodeState.Clean;
        Fault = null;
        var changed = PortSchema<TOut>.Fields.Where(f => f.Differs(previous, _output)).Select(f => f.Index).ToArray();

        IReadOnlyList<ExecOutput> fired = [.. finished.Context.FiredOutputs.Select(n => ExecOut(n))];
        if (fired.Count == 0 && IsAction && ExecOutputs.Count == 1)
        {
            fired = ExecOutputs;
        }
        return new AsyncCompletion(changed, IsAction ? fired : []);
    }

    private void Start(AsyncOperation<TIn, TOut> operation, GraphEvaluator evaluator)
    {
        _job?.Cancellation.Cancel();
        var cancellation = new CancellationTokenSource();
        var job = new AsyncJob(cancellation, new AsyncContext(Name, [.. ExecOutputs.Select(o => o.Name)], cancellation.Token));
        _job = job;
        State = NodeState.Running;
        Fault = null;

        var input = _input;
        var previous = _output;
        evaluator.AsyncStarted();
        job.Task = Task.Run(() => operation.RunAsync(input, previous, job.Context), cancellation.Token);
        job.Task.ContinueWith(_ => evaluator.AsyncFinished(this, job), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private sealed class AsyncJob(CancellationTokenSource cancellation, AsyncContext context)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public AsyncContext Context { get; } = context;
        public Task<TOut>? Task { get; set; }
    }

    private static bool AnyDiffers<T>(IReadOnlyList<PortField<T>> fields, in T a, in T b) where T : struct
    {
        foreach (var field in fields)
        {
            if (field.Differs(a, b))
            {
                return true;
            }
        }
        return false;
    }
}
