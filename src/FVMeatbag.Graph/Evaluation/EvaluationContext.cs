namespace FVMeatbag.Graph;

/// <summary>Passed to <see cref="Operation{TIn,TOut}.Execute"/>; gives an operation access to its node and graph.</summary>
public sealed class EvaluationContext
{
    internal EvaluationContext(GraphEvaluator evaluator, Node node, long pass)
    {
        Evaluator = evaluator;
        Node = node;
        Pass = pass;
    }

    public GraphEvaluator Evaluator { get; }
    public Node Node { get; }
    public Graph Graph => Node.Graph!;

    /// <summary>Monotonic id of the current evaluation pass.</summary>
    public long Pass { get; }
}
