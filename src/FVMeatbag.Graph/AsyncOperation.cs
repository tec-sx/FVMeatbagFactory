namespace FVMeatbag.Graph;

/// <summary>Non-generic marker for <see cref="AsyncOperation{TIn,TOut}"/>.</summary>
public interface IAsyncOperation;

/// <summary>
/// An operation too slow for the evaluation pass (a solve, a bake, an import): it runs on the
/// thread pool and its result lands in a later pass.
/// </summary>
/// <remarks>
/// <para>
/// When the node evaluates, <see cref="RunAsync"/> starts with a copy of the inputs and the node
/// becomes <see cref="NodeState.Running"/>. Its outputs keep their previous values and nothing
/// downstream runs until the work completes. The first <see cref="GraphEvaluator.Evaluate"/> after
/// that applies the result like a normal run: changed outputs propagate, a throw faults the node.
/// </para>
/// <para>
/// An input change while running cancels the work and starts it again with the new inputs; so does
/// removing the node, without a restart. A cancelled run's result is dropped.
/// </para>
/// <para>
/// <see cref="RunAsync"/> must not touch the graph: it gets snapshots and returns a new output
/// struct. Inputs follow the graph's rule of immutable or versioned data, which keeps that safe.
/// </para>
/// </remarks>
public abstract class AsyncOperation<TIn, TOut> : Operation<TIn, TOut>, IAsyncOperation
    where TIn : struct
    where TOut : struct
{
    /// <summary>Async operations are started by their node, never executed inline.</summary>
    public sealed override void Execute(in TIn input, ref TOut output, EvaluationContext context) =>
        throw new GraphException($"{Name} runs asynchronously; it is started by its node, not executed inline.");

    /// <summary>Does the work off the evaluation thread. <paramref name="previous"/> is the output before this run.</summary>
    public abstract Task<TOut> RunAsync(TIn input, TOut previous, AsyncContext context);
}

/// <summary>
/// An <see cref="AsyncOperation{TIn,TOut}"/> that runs only when triggered through an exec input,
/// like <see cref="ActionOperation{TIn,TOut}"/>. The chain continues once the work completes:
/// through the outputs fired with <see cref="AsyncContext.Fire"/>, or the single exec output if
/// it has one and nothing was fired. A failed or cancelled run stops the chain.
/// </summary>
public abstract class AsyncActionOperation<TIn, TOut> : AsyncOperation<TIn, TOut>, IActionOperation
    where TIn : struct
    where TOut : struct
{
    public virtual IReadOnlyList<string> ExecInputs => [ActionOperation<TIn, TOut>.DefaultInput];
    public virtual IReadOnlyList<string> ExecOutputs => [ActionOperation<TIn, TOut>.DefaultOutput];
}

/// <summary>Passed to <see cref="AsyncOperation{TIn,TOut}.RunAsync"/>. Safe to use from any thread.</summary>
public sealed class AsyncContext
{
    private readonly Lock _lock = new();
    private readonly List<string> _fired = [];
    private readonly IReadOnlyList<string> _execOutputs;
    private float _progress;

    internal AsyncContext(string nodeName, IReadOnlyList<string> execOutputs, CancellationToken cancellation)
    {
        NodeName = nodeName;
        _execOutputs = execOutputs;
        Cancellation = cancellation;
    }

    public string NodeName { get; }

    /// <summary>Cancelled when the inputs change again or the node is removed. Check it in long loops.</summary>
    public CancellationToken Cancellation { get; }

    /// <summary>Last reported progress, 0 to 1.</summary>
    public float Progress => Volatile.Read(ref _progress);

    /// <summary>Reports progress from 0 to 1; readable on the node as <see cref="Node.Progress"/>.</summary>
    public void ReportProgress(float fraction) => Volatile.Write(ref _progress, Math.Clamp(fraction, 0f, 1f));

    /// <summary>For async actions: continues the chain through the named exec output once the work completes.</summary>
    public void Fire(string execOutput)
    {
        if (!_execOutputs.Contains(execOutput))
        {
            throw new GraphException(_execOutputs.Count == 0
                ? $"{NodeName} is not an action; only actions can fire exec outputs."
                : $"{NodeName} has no exec output '{execOutput}'.");
        }
        lock (_lock)
        {
            _fired.Add(execOutput);
        }
    }

    internal IReadOnlyList<string> FiredOutputs
    {
        get
        {
            lock (_lock)
            {
                return [.. _fired];
            }
        }
    }
}
