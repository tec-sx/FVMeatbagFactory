using FVMeatbag.Graph.Ports;

namespace FVMeatbag.Graph;

public enum NodeState
{
    /// <summary>Outputs reflect the current inputs.</summary>
    Clean,

    /// <summary>An input changed; the node is queued for evaluation.</summary>
    Dirty,

    /// <summary>The last evaluation threw. Outputs keep their previous values and nothing downstream ran.</summary>
    Faulted,
}

/// <summary>
/// A black box in the graph: an input struct, an output struct and an operation between them.
/// See <see cref="Node{TIn,TOut}"/> for the typed implementation.
/// </summary>
public abstract class Node
{
    private protected Node(string name)
    {
        Name = name;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }

    /// <summary>The graph this node lives in, or null if it has not been added yet.</summary>
    public Graph? Graph { get; internal set; }

    public NodeState State { get; internal set; } = NodeState.Dirty;

    /// <summary>The exception from the last failed evaluation; cleared by the next successful one.</summary>
    public Exception? Fault { get; internal set; }

    public abstract IReadOnlyList<InputPort> Inputs { get; }
    public abstract IReadOnlyList<OutputPort> Outputs { get; }
    public abstract IReadOnlyList<ExecInput> ExecInputs { get; }
    public abstract IReadOnlyList<ExecOutput> ExecOutputs { get; }

    /// <summary>Action nodes run only when triggered through an exec input, never on data changes.</summary>
    public bool IsAction => ExecInputs.Count > 0 || ExecOutputs.Count > 0;

    /// <summary>Raised after an evaluation that changed at least one output.</summary>
    public event EventHandler<OutputsChangedEventArgs>? OutputsChanged;

    // Topological rank maintained by the evaluator. Upstream nodes always have a lower rank.
    internal int Rank { get; set; }
    internal bool IsQueued { get; set; }

    public Maybe<InputPort> FindInput(string name) => Inputs.FirstOrNone(p => p.Name == name);
    public Maybe<OutputPort> FindOutput(string name) => Outputs.FirstOrNone(p => p.Name == name);
    public Maybe<ExecInput> FindExecIn(string name) => ExecInputs.FirstOrNone(p => p.Name == name);
    public Maybe<ExecOutput> FindExecOut(string name) => ExecOutputs.FirstOrNone(p => p.Name == name);

    public InputPort Input(string name) => FindInput(name).OrElse(() => throw Missing("input", name));
    public OutputPort Output(string name) => FindOutput(name).OrElse(() => throw Missing("output", name));

    public ExecInput ExecIn(string name = ActionOperation<Unit, Unit>.DefaultInput) =>
        FindExecIn(name).OrElse(() => throw Missing("exec input", name));

    public ExecOutput ExecOut(string name = ActionOperation<Unit, Unit>.DefaultOutput) =>
        FindExecOut(name).OrElse(() => throw Missing("exec output", name));

    /// <summary>Runs this action node from the named exec input (immediately, or on the next evaluate in deferred mode).</summary>
    public void Trigger(string execInput = ActionOperation<Unit, Unit>.DefaultInput) =>
        (Graph ?? throw new GraphException($"{Name} is not in a graph.")).Evaluator.Trigger(ExecIn(execInput));

    /// <summary>Queues this node for re-evaluation. Action nodes only record that their inputs changed.</summary>
    public void MarkDirty()
    {
        State = NodeState.Dirty;
        if (!IsAction)
        {
            Graph?.Evaluator.Enqueue(this);
        }
    }

    /// <summary>Pulls connected inputs, runs the operation and returns the indices of outputs that changed.</summary>
    internal abstract IReadOnlyList<int> Evaluate(EvaluationContext context);

    internal void RaiseOutputsChanged(IReadOnlyList<OutputPort> changed) =>
        OutputsChanged?.Invoke(this, new OutputsChangedEventArgs(changed));

    public override string ToString() => Name;

    private KeyNotFoundException Missing(string kind, string name) => new($"{Name} has no {kind} '{name}'.");
}

/// <summary>An empty port struct, for operations without inputs or outputs.</summary>
public readonly struct Unit;

public sealed class OutputsChangedEventArgs(IReadOnlyList<OutputPort> changed) : EventArgs
{
    public IReadOnlyList<OutputPort> ChangedPorts { get; } = changed;
}
