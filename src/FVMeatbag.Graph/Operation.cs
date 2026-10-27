namespace FVMeatbag.Graph;

/// <summary>
/// The behaviour of a node: a pure-as-possible function from an input struct to an output struct.
/// </summary>
/// <remarks>
/// <para>
/// The <c>output</c> argument of <see cref="Execute"/> still holds the previous result, so an
/// operation can reuse cached data and only overwrite what changed. The runtime compares the old
/// and new output field by field and only propagates ports whose value actually changed.
/// </para>
/// <para>
/// Fields that carry heavy data (meshes, textures) should hold immutable or versioned references:
/// change detection uses <see cref="EqualityComparer{T}.Default"/>, which is reference equality for
/// most classes.
/// </para>
/// </remarks>
public abstract class Operation<TIn, TOut>
    where TIn : struct
    where TOut : struct
{
    /// <summary>Display name used when a node is created without an explicit name; drops an "Operation" or "Action" suffix.</summary>
    public virtual string Name
    {
        get
        {
            var name = GetType().Name;
            foreach (var suffix in (ReadOnlySpan<string>)["Operation", "Action"])
            {
                if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return name[..^suffix.Length];
                }
            }
            return name;
        }
    }

    public abstract void Execute(in TIn input, ref TOut output, EvaluationContext context);
}
