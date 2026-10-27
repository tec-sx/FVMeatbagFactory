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

    /// <summary>Raised after an evaluation that changed at least one output.</summary>
    public event EventHandler<OutputsChangedEventArgs>? OutputsChanged;

    // Topological rank maintained by the evaluator. Upstream nodes always have a lower rank.
    internal int Rank { get; set; }
    internal bool IsQueued { get; set; }

    public InputPort Input(string name) =>
        Inputs.FirstOrDefault(p => p.Name == name) ?? throw new KeyNotFoundException($"{Name} has no input '{name}'.");

    public OutputPort Output(string name) =>
        Outputs.FirstOrDefault(p => p.Name == name) ?? throw new KeyNotFoundException($"{Name} has no output '{name}'.");

    /// <summary>Queues this node for re-evaluation.</summary>
    public void MarkDirty()
    {
        State = NodeState.Dirty;
        Graph?.Evaluator.Enqueue(this);
    }

    /// <summary>Pulls connected inputs, runs the operation and returns the indices of outputs that changed.</summary>
    internal abstract IReadOnlyList<int> Evaluate(EvaluationContext context);

    internal void RaiseOutputsChanged(IReadOnlyList<OutputPort> changed) =>
        OutputsChanged?.Invoke(this, new OutputsChangedEventArgs(changed));

    public override string ToString() => Name;
}

public sealed class OutputsChangedEventArgs(IReadOnlyList<OutputPort> changed) : EventArgs
{
    public IReadOnlyList<OutputPort> ChangedPorts { get; } = changed;
}
