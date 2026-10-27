using FVMeatbag.Graph.Ports;

namespace FVMeatbag.Graph.Tests;

public struct BinaryInput
{
    public float A;
    public float B;
}

public struct ValueOutput
{
    public float Value;
}

public struct ValueInput
{
    public float Value;
}

public struct AnnotatedInput
{
    [Port("Brush Radius")]
    public float Radius;

    [PortIgnore]
    public int Scratch;

    public string Label;
}

public struct LabelOutput
{
    public string Label;
}

/// <summary>Base for test operations that counts how often it ran.</summary>
public abstract class CountingOperation<TIn, TOut> : Operation<TIn, TOut>
    where TIn : struct
    where TOut : struct
{
    public int Executions { get; private set; }

    public override void Execute(in TIn input, ref TOut output, EvaluationContext context)
    {
        Executions++;
        Run(input, ref output);
    }

    protected abstract void Run(in TIn input, ref TOut output);
}

public sealed class AddOperation : CountingOperation<BinaryInput, ValueOutput>
{
    protected override void Run(in BinaryInput input, ref ValueOutput output) => output.Value = input.A + input.B;
}

public sealed class MultiplyOperation : CountingOperation<BinaryInput, ValueOutput>
{
    protected override void Run(in BinaryInput input, ref ValueOutput output) => output.Value = input.A * input.B;
}

public sealed class PassThroughOperation : CountingOperation<ValueInput, ValueOutput>
{
    protected override void Run(in ValueInput input, ref ValueOutput output) => output.Value = input.Value;
}

/// <summary>Outputs the sign of its input, so most input changes do not change the output.</summary>
public sealed class SignOperation : CountingOperation<ValueInput, ValueOutput>
{
    protected override void Run(in ValueInput input, ref ValueOutput output) => output.Value = MathF.Sign(input.Value);
}

public sealed class ThrowWhenNegativeOperation : CountingOperation<ValueInput, ValueOutput>
{
    protected override void Run(in ValueInput input, ref ValueOutput output)
    {
        if (input.Value < 0)
        {
            throw new InvalidOperationException("negative");
        }
        output.Value = input.Value;
    }
}

public sealed class LabelOperation : CountingOperation<AnnotatedInput, LabelOutput>
{
    protected override void Run(in AnnotatedInput input, ref LabelOutput output) => output.Label = input.Label;
}
