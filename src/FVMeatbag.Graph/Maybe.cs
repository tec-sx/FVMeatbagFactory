using System.Diagnostics.CodeAnalysis;

namespace FVMeatbag.Graph;

/// <summary>
/// A value that may be absent. Use it for ports and lookups where "nothing yet" is a normal state
/// (no mesh loaded, no project, port not found) rather than an error.
/// </summary>
/// <remarks>
/// Equality compares presence and then the value with <see cref="EqualityComparer{T}.Default"/>,
/// so change detection on ports treats None → None as unchanged. <c>default(Maybe&lt;T&gt;)</c> is None,
/// which makes it the natural initial state of a port struct field.
/// </remarks>
public readonly struct Maybe<T> : IEquatable<Maybe<T>>
{
    private readonly T _value;

    private Maybe(T value)
    {
        _value = value;
        HasValue = true;
    }

    public static Maybe<T> None => default;

    public bool HasValue { get; }
    public bool IsNone => !HasValue;

    /// <summary>The value. Throws if there is none; prefer <see cref="TryGetValue"/>, <see cref="Match{TResult}"/> or <see cref="OrThrow"/>.</summary>
    public T Value => HasValue ? _value : throw new InvalidOperationException($"Maybe<{typeof(T).Name}> has no value.");

    /// <summary>Wraps <paramref name="value"/>; a null reference becomes None.</summary>
    public static Maybe<T> Some(T value) => value is null ? None : new Maybe<T>(value);

    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return HasValue;
    }

    public T OrDefault(T fallback) => HasValue ? _value : fallback;
    public T OrElse(Func<T> fallback) => HasValue ? _value : fallback();

    /// <summary>The value, or an <see cref="InvalidOperationException"/> with <paramref name="message"/>. Handy in actions, where a throw faults the node.</summary>
    public T OrThrow(string message) => HasValue ? _value : throw new InvalidOperationException(message);

    public Maybe<TResult> Map<TResult>(Func<T, TResult> map) => HasValue ? Maybe<TResult>.Some(map(_value)) : Maybe<TResult>.None;
    public Maybe<TResult> Bind<TResult>(Func<T, Maybe<TResult>> bind) => HasValue ? bind(_value) : Maybe<TResult>.None;
    public Maybe<T> Where(Func<T, bool> predicate) => HasValue && predicate(_value) ? this : None;
    public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none) => HasValue ? some(_value) : none();

    public bool Equals(Maybe<T> other) =>
        HasValue == other.HasValue && (!HasValue || EqualityComparer<T>.Default.Equals(_value, other._value));

    public override bool Equals(object? obj) => obj is Maybe<T> other && Equals(other);
    public override int GetHashCode() => HasValue ? HashCode.Combine(true, _value) : 0;
    public override string ToString() => HasValue ? $"Some({_value})" : "None";

    public static bool operator ==(Maybe<T> left, Maybe<T> right) => left.Equals(right);
    public static bool operator !=(Maybe<T> left, Maybe<T> right) => !left.Equals(right);

#pragma warning disable CA2225 // Some() is the named alternative
    public static implicit operator Maybe<T>(T value) => Some(value);
#pragma warning restore CA2225
}

public static class Maybe
{
    public static Maybe<T> Some<T>(T value) => Maybe<T>.Some(value);

    /// <summary>Turns a nullable reference into a Maybe.</summary>
    public static Maybe<T> From<T>(T? value) where T : class => value is null ? Maybe<T>.None : Maybe<T>.Some(value);

    /// <summary>Turns a nullable value type into a Maybe.</summary>
    public static Maybe<T> From<T>(T? value) where T : struct => value.HasValue ? Maybe<T>.Some(value.Value) : Maybe<T>.None;

    /// <summary>The first element matching <paramref name="predicate"/>, or None.</summary>
    public static Maybe<T> FirstOrNone<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        foreach (var item in source)
        {
            if (predicate(item))
            {
                return Some(item);
            }
        }
        return Maybe<T>.None;
    }
}
