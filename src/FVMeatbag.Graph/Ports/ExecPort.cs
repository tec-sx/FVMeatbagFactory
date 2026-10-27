namespace FVMeatbag.Graph.Ports;

/// <summary>
/// An execution pin. Exec wires carry no value: they say "run this action next", which keeps the
/// order of side-effecting steps (import, create, add to scene) explicit.
/// </summary>
public abstract class ExecPort
{
    private protected ExecPort(Node node, string name, int index)
    {
        Node = node;
        Name = name;
        Index = index;
    }

    public Node Node { get; }
    public string Name { get; }
    public int Index { get; }
    public abstract PortDirection Direction { get; }

    public override string ToString() => $"{Node.Name}.{Name}";
}

/// <summary>An entry point of an action node. Any number of exec outputs may lead into it.</summary>
public sealed class ExecInput : ExecPort
{
    private readonly List<ExecEdge> _connections = [];

    internal ExecInput(Node node, string name, int index) : base(node, name, index)
    {
    }

    public override PortDirection Direction => PortDirection.Input;
    public IReadOnlyList<ExecEdge> Connections => _connections;

    internal void AddConnection(ExecEdge edge) => _connections.Add(edge);
    internal void RemoveConnection(ExecEdge edge) => _connections.Remove(edge);
}

/// <summary>An exit of an action node. Leads to at most one exec input, like a Blueprint "then" pin.</summary>
public sealed class ExecOutput : ExecPort
{
    internal ExecOutput(Node node, string name, int index) : base(node, name, index)
    {
    }

    public override PortDirection Direction => PortDirection.Output;
    public ExecEdge? Connection { get; internal set; }
}
