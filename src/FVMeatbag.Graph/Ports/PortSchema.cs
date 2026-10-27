using System.Linq.Expressions;
using System.Reflection;

namespace FVMeatbag.Graph.Ports;

/// <summary>
/// Describes the ports of an input or output struct. Built once per struct type via reflection and
/// compiled into by-ref accessors, so reading and writing a port never boxes.
/// </summary>
/// <remarks>
/// A source generator can replace the reflection step later (for trimming / NativeAOT) without
/// changing any public API.
/// </remarks>
public static class PortSchema<TStruct> where TStruct : struct
{
    public static IReadOnlyList<PortField<TStruct>> Fields { get; } = Build();

    private static PortField<TStruct>[] Build()
    {
        var fields = typeof(TStruct)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.GetCustomAttribute<PortIgnoreAttribute>() is null)
            .OrderBy(f => f.MetadataToken)
            .ToArray();

        var result = new PortField<TStruct>[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            var fieldType = typeof(PortField<,>).MakeGenericType(typeof(TStruct), fields[i].FieldType);
            result[i] = (PortField<TStruct>)Activator.CreateInstance(
                fieldType, BindingFlags.NonPublic | BindingFlags.Instance, null, [fields[i], i], null)!;
        }
        return result;
    }
}

/// <summary>A single field of a port struct.</summary>
public abstract class PortField<TStruct> where TStruct : struct
{
    private protected PortField(FieldInfo field, int index)
    {
        Name = field.Name;
        DisplayName = field.GetCustomAttribute<PortAttribute>()?.DisplayName ?? field.Name;
        Index = index;
    }

    public string Name { get; }
    public string DisplayName { get; }
    public int Index { get; }
    public abstract Type ValueType { get; }

    /// <summary>True when the field differs between <paramref name="a"/> and <paramref name="b"/>.</summary>
    public abstract bool Differs(in TStruct a, in TStruct b);

    internal abstract InputPort CreateInputPort<TOut>(Node<TStruct, TOut> node) where TOut : struct;
    internal abstract OutputPort CreateOutputPort<TIn>(Node<TIn, TStruct> node) where TIn : struct;
}

/// <summary>Typed accessor for one field of a port struct.</summary>
public sealed class PortField<TStruct, TValue> : PortField<TStruct> where TStruct : struct
{
    public delegate TValue RefGetter(ref TStruct target);
    public delegate void RefSetter(ref TStruct target, TValue value);

    private static readonly EqualityComparer<TValue> Comparer = EqualityComparer<TValue>.Default;

    private PortField(FieldInfo field, int index) : base(field, index)
    {
        var target = Expression.Parameter(typeof(TStruct).MakeByRefType(), "target");
        var value = Expression.Parameter(typeof(TValue), "value");
        var access = Expression.Field(target, field);

        Get = Expression.Lambda<RefGetter>(access, target).Compile();
        Set = Expression.Lambda<RefSetter>(Expression.Assign(access, value), target, value).Compile();
    }

    public RefGetter Get { get; }
    public RefSetter Set { get; }
    public override Type ValueType => typeof(TValue);

    public override bool Differs(in TStruct a, in TStruct b)
    {
        var left = a;
        var right = b;
        return !Comparer.Equals(Get(ref left), Get(ref right));
    }

    internal override InputPort CreateInputPort<TOut>(Node<TStruct, TOut> node) =>
        new InputPort<TStruct, TOut, TValue>(node, this);

    internal override OutputPort CreateOutputPort<TIn>(Node<TIn, TStruct> node) =>
        new OutputPort<TIn, TStruct, TValue>(node, this);
}
