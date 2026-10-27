using FVMeatbag.Graph.Ports;

namespace FVMeatbag.Graph;

public enum EvaluationMode
{
    /// <summary>Every change outside a batch evaluates the graph right away. Handy for tests and tools.</summary>
    Immediate,

    /// <summary>Changes only queue work; the host calls <see cref="GraphEvaluator.Evaluate"/> once per frame or tick.</summary>
    Deferred,
}

/// <summary>
/// Change propagation for one root graph and all of its child graphs.
/// </summary>
/// <remarks>
/// <para>
/// Setting an input marks its node dirty and queues it. <see cref="Evaluate"/> drains the queue in
/// topological order (lowest rank first), so a node runs once per pass and only after everything
/// upstream of it has settled. That keeps diamond-shaped chains glitch-free, which a naive
/// "each input change fires an event that recalculates" design does not.
/// </para>
/// <para>
/// After a node runs, only the outputs that actually changed dirty their downstream nodes, so a
/// change stops propagating as soon as a result comes out the same.
/// </para>
/// <para>
/// Action nodes are the exception: they run only when an exec chain reaches them (see
/// <see cref="Trigger"/>), with the data graph settled before every step.
/// </para>
/// <para>Not thread-safe: own it from one thread (the UI or a dedicated graph thread).</para>
/// </remarks>
public sealed class GraphEvaluator
{
    private readonly HashSet<Node> _nodes = [];
    private readonly PriorityQueue<Node, (int Rank, long Sequence)> _queue = new();
    private readonly Queue<ExecInput> _triggers = new();
    private long _sequence;
    private long _pass;
    private int _evaluatedThisPass;
    private int _batchDepth;
    private bool _evaluating;
    private bool _ranksStale;

    internal GraphEvaluator(EvaluationMode mode)
    {
        Mode = mode;
    }

    public EvaluationMode Mode { get; set; }

    /// <summary>Safety valve against operations that keep re-dirtying each other through side effects.</summary>
    public int MaxEvaluationsPerPass { get; set; } = 100_000;

    public bool HasPendingWork => _queue.Count > 0 || _triggers.Count > 0;
    public bool IsBatching => _batchDepth > 0;
    public bool IsEvaluating => _evaluating;

    public event EventHandler<NodeEvaluatedEventArgs>? NodeEvaluated;
    public event EventHandler<NodeFaultedEventArgs>? NodeFaulted;

    /// <summary>
    /// Suspends immediate evaluation until the returned scope is disposed, then evaluates once.
    /// Use it to apply many edits (e.g. a whole sculpt stroke or an import) as one change.
    /// </summary>
    public IDisposable BeginBatch()
    {
        _batchDepth++;
        return new BatchScope(this);
    }

    /// <summary>
    /// Evaluates every dirty data node and its affected downstream chain, then runs queued action
    /// chains in trigger order. Returns how many nodes ran.
    /// </summary>
    public int Evaluate()
    {
        if (_evaluating)
        {
            return 0;
        }

        _evaluating = true;
        _pass++;
        _evaluatedThisPass = 0;
        try
        {
            SettleData();
            while (_triggers.TryDequeue(out var trigger))
            {
                RunActionChain(trigger);
            }
        }
        finally
        {
            _evaluating = false;
        }
        return _evaluatedThisPass;
    }

    /// <summary>
    /// Queues an action chain starting at <paramref name="entry"/>. Chains run after the data graph
    /// has settled, one after another, each to completion.
    /// </summary>
    public void Trigger(ExecInput entry)
    {
        if (entry.Node.Graph?.Evaluator != this)
        {
            throw new GraphException($"{entry} is not part of this graph tree.");
        }
        _triggers.Enqueue(entry);
        RequestEvaluation();
    }

    private void SettleData()
    {
        while (_queue.Count > 0)
        {
            if (_ranksStale)
            {
                RecomputeRanks();
            }

            var node = _queue.Dequeue();
            node.IsQueued = false;
            if (node.Graph is not null && !node.IsAction)
            {
                Run(node, null);
            }
        }
    }

    // Depth first: everything an action fires completes before the next queued trigger starts.
    private void RunActionChain(ExecInput entry)
    {
        var pending = new Stack<ExecInput>();
        pending.Push(entry);
        while (pending.Count > 0)
        {
            var pin = pending.Pop();
            if (pin.Node.Graph is null)
            {
                continue; // removed by an earlier step
            }

            SettleData(); // the action must see up-to-date inputs, including results of earlier steps
            var context = Run(pin.Node, pin);
            if (context is null)
            {
                continue; // faulted: the chain stops here
            }

            var fired = context.FiredOutputs;
            if (fired.Count == 0 && pin.Node.ExecOutputs.Count == 1)
            {
                fired = pin.Node.ExecOutputs;
            }
            for (var i = fired.Count - 1; i >= 0; i--)
            {
                if (fired[i].Connection is { } edge)
                {
                    pending.Push(edge.Target);
                }
            }
        }
        SettleData();
    }

    /// <summary>Runs one node and propagates changed outputs. Returns null if the node faulted.</summary>
    private EvaluationContext? Run(Node node, ExecInput? trigger)
    {
        if (++_evaluatedThisPass > MaxEvaluationsPerPass)
        {
            throw new GraphException(
                $"Evaluation pass exceeded {MaxEvaluationsPerPass} node runs; the graph is probably re-triggering itself in a loop.");
        }

        var context = new EvaluationContext(this, node, _pass, trigger);
        var changed = node.Evaluate(context);
        if (node.State == NodeState.Faulted)
        {
            NodeFaulted?.Invoke(this, new NodeFaultedEventArgs(node, node.Fault!));
            return null;
        }

        var changedPorts = new OutputPort[changed.Count];
        for (var i = 0; i < changed.Count; i++)
        {
            var port = node.Outputs[changed[i]];
            changedPorts[i] = port;
            foreach (var edge in port.Connections)
            {
                edge.Target.Node.MarkDirty();
            }
        }

        NodeEvaluated?.Invoke(this, new NodeEvaluatedEventArgs(node, changedPorts));
        if (changedPorts.Length > 0)
        {
            node.RaiseOutputsChanged(changedPorts);
        }
        return context;
    }

    internal void Register(Node node)
    {
        _nodes.Add(node);
        _ranksStale = true;
        node.MarkDirty();
    }

    internal void Unregister(Node node)
    {
        _nodes.Remove(node);
        _ranksStale = true;
    }

    internal void TopologyChanged() => _ranksStale = true;

    internal void Enqueue(Node node)
    {
        if (!node.IsQueued)
        {
            node.IsQueued = true;
            _queue.Enqueue(node, (node.Rank, _sequence++));
        }
        RequestEvaluation();
    }

    /// <summary>True if <paramref name="to"/> can be reached from <paramref name="from"/> by following edges downstream.</summary>
    internal static bool IsDownstream(Node from, Node to)
    {
        var visited = new HashSet<Node>();
        var stack = new Stack<Node>();
        stack.Push(from);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == to)
            {
                return true;
            }
            if (!visited.Add(current))
            {
                continue;
            }
            foreach (var output in current.Outputs)
            {
                foreach (var edge in output.Connections)
                {
                    stack.Push(edge.Target.Node);
                }
            }
        }
        return false;
    }

    private void RequestEvaluation()
    {
        if (Mode == EvaluationMode.Immediate && _batchDepth == 0 && !_evaluating)
        {
            Evaluate();
        }
    }

    private void EndBatch()
    {
        if (--_batchDepth == 0)
        {
            RequestEvaluation();
        }
    }

    // Kahn's algorithm: rank = 1 + highest rank of any upstream node. Cycles are rejected at connect time.
    private void RecomputeRanks()
    {
        var pendingInputs = new Dictionary<Node, int>(_nodes.Count);
        var ready = new Queue<Node>();
        foreach (var node in _nodes)
        {
            var upstream = node.Inputs.Count(i => i.Connection is not null);
            pendingInputs[node] = upstream;
            node.Rank = 0;
            if (upstream == 0)
            {
                ready.Enqueue(node);
            }
        }

        while (ready.Count > 0)
        {
            var node = ready.Dequeue();
            foreach (var output in node.Outputs)
            {
                foreach (var edge in output.Connections)
                {
                    var target = edge.Target.Node;
                    target.Rank = Math.Max(target.Rank, node.Rank + 1);
                    if (--pendingInputs[target] == 0)
                    {
                        ready.Enqueue(target);
                    }
                }
            }
        }

        // Re-key everything still queued with the new ranks.
        var queued = new List<Node>(_queue.Count);
        while (_queue.TryDequeue(out var node, out _))
        {
            queued.Add(node);
        }
        foreach (var node in queued)
        {
            _queue.Enqueue(node, (node.Rank, _sequence++));
        }
        _ranksStale = false;
    }

    private sealed class BatchScope(GraphEvaluator evaluator) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                evaluator.EndBatch();
            }
        }
    }
}

public sealed class NodeEvaluatedEventArgs(Node node, IReadOnlyList<OutputPort> changedPorts) : EventArgs
{
    public Node Node { get; } = node;
    public IReadOnlyList<OutputPort> ChangedPorts { get; } = changedPorts;
}

public sealed class NodeFaultedEventArgs(Node node, Exception exception) : EventArgs
{
    public Node Node { get; } = node;
    public Exception Exception { get; } = exception;
}
