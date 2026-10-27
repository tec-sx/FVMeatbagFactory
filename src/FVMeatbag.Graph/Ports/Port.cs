namespace FVMeatbag.Graph.Ports;

public enum PortDirection
{
    Input,
    Output,
}

/// <summary>A named, typed connection point on a node, backed by one field of its input or output struct.</summary>
public abstract class Port
{
    private protected Port(Node node, string name, string displayName, int index)
    {
        Node = node;
        Name = name;
        DisplayName = displayName;
        Index = index;
    }

    public Node Node { get; }
    public string Name { get; }
    public string DisplayName { get; }
    public int Index { get; }
    public abstract Type ValueType { get; }
    public abstract PortDirection Direction { get; }

    public override string ToString() => $"{Node.Name}.{Name}";
}

/// <summary>An input port. Holds at most one incoming edge.</summary>
public abstract class InputPort : Port
{
    private protected InputPort(Node node, string name, string displayName, int index)
        : base(node, name, displayName, index)
    {
    }

    public override PortDirection Direction => PortDirection.Input;
    public Edge? Connection { get; internal set; }
    public bool IsConnected => Connection is not null;

    /// <summary>Reads the current value of this input.</summary>
    public abstract T GetValue<T>();

    /// <summary>
    /// Sets the local value of this input and marks the node dirty if it changed.
    /// A connected input is overwritten by its upstream value on the next evaluation.
    /// </summary>
    public abstract void SetValue<T>(T value);
}

/// <summary>An output port. Fans out to any number of edges.</summary>
public abstract class OutputPort : Port
{
    private readonly List<Edge> _connections = [];

    private protected OutputPort(Node node, string name, string displayName, int index)
        : base(node, name, displayName, index)
    {
    }

    public override PortDirection Direction => PortDirection.Output;
    public IReadOnlyList<Edge> Connections => _connections;

    /// <summary>Reads the current value of this output.</summary>
    public abstract T GetValue<T>();

    internal void AddConnection(Edge edge) => _connections.Add(edge);
    internal void RemoveConnection(Edge edge) => _connections.Remove(edge);

    /// <summary>Creates the typed copy routine used by an edge from this port to <paramref name="target"/>.</summary>
    internal abstract Action CreateTransfer(InputPort target);
}

internal static class PortValue
{
    public static TTo Cast<TFrom, TTo>(TFrom value, Port port) => value switch
    {
        TTo typed => typed,
        null when default(TTo) is null => default!,
        _ => throw new InvalidCastException(
            $"Port {port} holds {port.ValueType.Name}, which is not compatible with {typeof(TTo).Name}."),
    };
}

internal interface IValueSink<in TValue>
{
    void Assign(TValue value);
}

internal sealed class InputPort<TIn, TOut, TValue>(Node<TIn, TOut> node, PortField<TIn, TValue> field)
    : InputPort(node, field.Name, field.DisplayName, field.Index), IValueSink<TValue>
    where TIn : struct
    where TOut : struct
{
    public override Type ValueType => typeof(TValue);

    public override T GetValue<T>() => PortValue.Cast<TValue, T>(field.Get(ref node.InputRef), this);

    public override void SetValue<T>(T value)
    {
        var typed = PortValue.Cast<T, TValue>(value, this);
        if (EqualityComparer<TValue>.Default.Equals(field.Get(ref node.InputRef), typed))
        {
            return;
        }
        field.Set(ref node.InputRef, typed);
        node.MarkDirty();
    }

    // Called by edges during evaluation; the node is already being evaluated so no dirty marking.
    public void Assign(TValue value) => field.Set(ref node.InputRef, value);
}

internal sealed class OutputPort<TIn, TOut, TValue>(Node<TIn, TOut> node, PortField<TOut, TValue> field)
    : OutputPort(node, field.Name, field.DisplayName, field.Index)
    where TIn : struct
    where TOut : struct
{
    public override Type ValueType => typeof(TValue);

    public override T GetValue<T>() => PortValue.Cast<TValue, T>(field.Get(ref node.OutputRef), this);

    internal override Action CreateTransfer(InputPort target)
    {
        if (target is not IValueSink<TValue> sink)
        {
            throw new GraphException(
                $"Cannot connect {this} ({typeof(TValue).Name}) to {target} ({target.ValueType.Name}). " +
                "Port types must match exactly; insert a conversion node.");
        }
        return () => sink.Assign(field.Get(ref node.OutputRef));
    }
}
