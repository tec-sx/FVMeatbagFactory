using FVMeatbag.Graph.Ports;

namespace FVMeatbag.Graph;

/// <summary>Mutates an input struct in place.</summary>
public delegate void InputUpdate<TIn>(ref TIn input) where TIn : struct;

/// <summary>A node whose ports are the public fields of <typeparamref name="TIn"/> and <typeparamref name="TOut"/>.</summary>
public class Node<TIn, TOut> : Node
    where TIn : struct
    where TOut : struct
{
    private static readonly IReadOnlyList<int> NoChanges = [];

    private TIn _input;
    private TOut _output;

    public Node(Operation<TIn, TOut> operation, string? name = null, TIn initialInput = default)
        : base(name ?? operation.Name)
    {
        Operation = operation;
        _input = initialInput;
        Inputs = PortSchema<TIn>.Fields.Select(f => f.CreateInputPort(this)).ToArray();
        Outputs = PortSchema<TOut>.Fields.Select(f => f.CreateOutputPort(this)).ToArray();
    }

    public Operation<TIn, TOut> Operation { get; }
    public override IReadOnlyList<InputPort> Inputs { get; }
    public override IReadOnlyList<OutputPort> Outputs { get; }

    /// <summary>A copy of the current input values.</summary>
    public TIn InputValues => _input;

    /// <summary>A copy of the current output values.</summary>
    public TOut OutputValues => _output;

    internal ref TIn InputRef => ref _input;
    internal ref TOut OutputRef => ref _output;

    /// <summary>Replaces the input struct. The node is marked dirty only if a field changed.</summary>
    public void SetInputs(in TIn input)
    {
        var changed = AnyDiffers(PortSchema<TIn>.Fields, _input, input);
        _input = input;
        if (changed)
        {
            MarkDirty();
        }
    }

    /// <summary>Edits the input struct in place, e.g. <c>node.UpdateInputs((ref BrushInput i) => i.Radius = 2f)</c>.</summary>
    public void UpdateInputs(InputUpdate<TIn> update)
    {
        var next = _input;
        update(ref next);
        SetInputs(next);
    }

    internal override IReadOnlyList<int> Evaluate(EvaluationContext context)
    {
        foreach (var input in Inputs)
        {
            input.Connection?.Transfer();
        }

        var previous = _output;
        try
        {
            Operation.Execute(in _input, ref _output, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _output = previous;
            State = NodeState.Faulted;
            Fault = ex;
            return NoChanges;
        }

        State = NodeState.Clean;
        Fault = null;

        List<int>? changed = null;
        foreach (var field in PortSchema<TOut>.Fields)
        {
            if (field.Differs(previous, _output))
            {
                (changed ??= []).Add(field.Index);
            }
        }
        return changed ?? NoChanges;
    }

    private static bool AnyDiffers<T>(IReadOnlyList<PortField<T>> fields, in T a, in T b) where T : struct
    {
        foreach (var field in fields)
        {
            if (field.Differs(a, b))
            {
                return true;
            }
        }
        return false;
    }
}
