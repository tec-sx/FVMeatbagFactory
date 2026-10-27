using FVMeatbag.Graph.Ports;

namespace FVMeatbag.Graph;

/// <summary>A connection from an output port to an input port of the same type.</summary>
public sealed class Edge
{
    private readonly Action _transfer;

    internal Edge(OutputPort source, InputPort target)
    {
        Source = source;
        Target = target;
        _transfer = source.CreateTransfer(target);
    }

    public OutputPort Source { get; }
    public InputPort Target { get; }

    /// <summary>Copies the source value into the target input. Runs right before the target evaluates.</summary>
    internal void Transfer() => _transfer();

    public override string ToString() => $"{Source} -> {Target}";
}
