namespace FVMeatbag.Graph;

/// <summary>Non-generic view of <see cref="ActionOperation{TIn,TOut}"/>, used to build exec pins.</summary>
public interface IActionOperation
{
    IReadOnlyList<string> ExecInputs { get; }
    IReadOnlyList<string> ExecOutputs { get; }
}

/// <summary>
/// An operation with side effects that runs only when one of its exec inputs is triggered, never
/// because a data input changed. Data inputs are pulled (and everything upstream settled) right
/// before it runs; changed data outputs propagate as usual.
/// </summary>
/// <remarks>
/// Call <see cref="EvaluationContext.Fire(string)"/> to choose which exec outputs continue the chain.
/// If the action fires nothing and has exactly one exec output, that output fires automatically.
/// Throwing stops the chain.
/// </remarks>
public abstract class ActionOperation<TIn, TOut> : Operation<TIn, TOut>, IActionOperation
    where TIn : struct
    where TOut : struct
{
    public const string DefaultInput = "Execute";
    public const string DefaultOutput = "Then";

    public virtual IReadOnlyList<string> ExecInputs => [DefaultInput];
    public virtual IReadOnlyList<string> ExecOutputs => [DefaultOutput];
}
