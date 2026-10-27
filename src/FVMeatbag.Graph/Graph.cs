using FVMeatbag.Graph.Ports;

namespace FVMeatbag.Graph;

/// <summary>
/// A container of nodes. Graphs form a tree under one root; every graph in the tree shares the
/// root's <see cref="GraphEvaluator"/>, so nodes in different child graphs can be connected and are
/// evaluated in one consistent order.
/// </summary>
/// <remarks>
/// Child graphs are about ownership and lifetime: a scene component's behaviour graph, a gizmo's
/// interaction graph or a tool's graph is a child that is torn down together with its owner.
/// </remarks>
public sealed class Graph
{
    private readonly List<Node> _nodes = [];
    private readonly List<Graph> _children = [];

    private Graph(string name, Graph? parent, GraphEvaluator evaluator)
    {
        Name = name;
        Parent = parent;
        Evaluator = evaluator;
    }

    public string Name { get; set; }
    public Graph? Parent { get; private set; }
    public Graph Root => Parent?.Root ?? this;
    public bool IsRoot => Parent is null;

    /// <summary>False once the graph has been removed from its parent; a detached graph cannot be used again.</summary>
    public bool IsAttached { get; private set; } = true;

    public GraphEvaluator Evaluator { get; }
    public IReadOnlyList<Node> Nodes => _nodes;
    public IReadOnlyList<Graph> Children => _children;

    public static Graph CreateRoot(string name = "Root", EvaluationMode mode = EvaluationMode.Immediate) =>
        new(name, null, new GraphEvaluator(mode));

    public Graph CreateChild(string name)
    {
        EnsureAttached();
        var child = new Graph(name, this, Evaluator);
        _children.Add(child);
        return child;
    }

    /// <summary>Removes a child graph together with all of its nodes, connections and descendants.</summary>
    public void RemoveChild(Graph child)
    {
        if (child.Parent != this)
        {
            throw new GraphException($"{child.Name} is not a child of {Name}.");
        }
        using (Evaluator.BeginBatch())
        {
            child.Clear();
        }
        _children.Remove(child);
        child.Parent = null;
        child.IsAttached = false;
    }

    public TNode Add<TNode>(TNode node) where TNode : Node
    {
        EnsureAttached();
        if (node.Graph is not null)
        {
            throw new GraphException($"{node.Name} already belongs to graph {node.Graph.Name}.");
        }
        _nodes.Add(node);
        node.Graph = this;
        Evaluator.Register(node);
        return node;
    }

    /// <summary>Creates a node for <paramref name="operation"/> and adds it to this graph.</summary>
    public Node<TIn, TOut> AddNode<TIn, TOut>(Operation<TIn, TOut> operation, string? name = null, TIn initialInput = default)
        where TIn : struct
        where TOut : struct =>
        Add(new Node<TIn, TOut>(operation, name, initialInput));

    public void Remove(Node node)
    {
        if (node.Graph != this)
        {
            throw new GraphException($"{node.Name} does not belong to graph {Name}.");
        }
        using (Evaluator.BeginBatch())
        {
            DisconnectAll(node);
        }
        _nodes.Remove(node);
        node.Graph = null;
        Evaluator.Unregister(node);
    }

    /// <summary>
    /// Connects <paramref name="source"/> to <paramref name="target"/>. Either node may live in any
    /// graph of the same tree. An existing connection on <paramref name="target"/> is replaced.
    /// </summary>
    /// <exception cref="GraphCycleException">The connection would make the graph cyclic.</exception>
    public Edge Connect(OutputPort source, InputPort target)
    {
        EnsureAttached();
        EnsureInTree(source.Node);
        EnsureInTree(target.Node);

        if (GraphEvaluator.IsDownstream(target.Node, source.Node))
        {
            throw new GraphCycleException(
                $"Connecting {source} to {target} would create a cycle. Feedback needs an explicit state/delay node.");
        }

        var edge = new Edge(source, target); // validates port types before anything is mutated
        using (Evaluator.BeginBatch())
        {
            if (target.Connection is { } existing)
            {
                Detach(existing);
            }
            source.AddConnection(edge);
            target.Connection = edge;
            Evaluator.TopologyChanged();
            target.Node.MarkDirty();
        }
        return edge;
    }

    public void Disconnect(Edge edge)
    {
        if (edge.Target.Connection != edge)
        {
            return;
        }
        using (Evaluator.BeginBatch())
        {
            Detach(edge);
        }
    }

    /// <summary>All graphs from this one down, depth first.</summary>
    public IEnumerable<Graph> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in _children)
        {
            foreach (var graph in child.DescendantsAndSelf())
            {
                yield return graph;
            }
        }
    }

    public override string ToString() => Parent is null ? Name : $"{Parent}/{Name}";

    private void Clear()
    {
        foreach (var child in _children.ToArray())
        {
            RemoveChild(child);
        }
        foreach (var node in _nodes.ToArray())
        {
            Remove(node);
        }
    }

    private void DisconnectAll(Node node)
    {
        foreach (var input in node.Inputs)
        {
            if (input.Connection is { } edge)
            {
                Detach(edge);
            }
        }
        foreach (var output in node.Outputs)
        {
            foreach (var edge in output.Connections.ToArray())
            {
                Detach(edge);
            }
        }
    }

    // The target keeps the last value it received; it is re-evaluated in case it depends on being connected.
    private void Detach(Edge edge)
    {
        edge.Source.RemoveConnection(edge);
        edge.Target.Connection = null;
        Evaluator.TopologyChanged();
        edge.Target.Node.MarkDirty();
    }

    private void EnsureAttached()
    {
        if (!IsAttached)
        {
            throw new GraphException($"Graph {Name} has been removed from its parent.");
        }
    }

    private void EnsureInTree(Node node)
    {
        if (node.Graph is null || node.Graph.Evaluator != Evaluator)
        {
            throw new GraphException($"{node.Name} is not part of the graph tree rooted at {Root.Name}.");
        }
    }
}
