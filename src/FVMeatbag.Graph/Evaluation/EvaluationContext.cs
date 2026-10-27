using FVMeatbag.Graph.Ports;

namespace FVMeatbag.Graph;

/// <summary>Passed to <see cref="Operation{TIn,TOut}.Execute"/>; gives an operation access to its node and graph.</summary>
public sealed class EvaluationContext
{
    private List<ExecOutput>? _fired;

    internal EvaluationContext(GraphEvaluator evaluator, Node node, long pass, ExecInput? trigger = null)
    {
        Evaluator = evaluator;
        Node = node;
        Pass = pass;
        Trigger = trigger;
    }

    public GraphEvaluator Evaluator { get; }
    public Node Node { get; }
    public Graph Graph => Node.Graph!;

    /// <summary>Monotonic id of the current evaluation pass.</summary>
    public long Pass { get; }

    /// <summary>The exec input that started this run, or null for a data-driven evaluation.</summary>
    public ExecInput? Trigger { get; }

    internal IReadOnlyList<ExecOutput> FiredOutputs => _fired ?? (IReadOnlyList<ExecOutput>)[];

    /// <summary>Continues the chain through the named exec output once this action returns. Outputs run in the order fired.</summary>
    public void Fire(string execOutput)
    {
        if (Trigger is null)
        {
            throw new GraphException($"{Node.Name} is not running as an action; only actions can fire exec outputs.");
        }
        (_fired ??= []).Add(Node.ExecOut(execOutput));
    }
}
