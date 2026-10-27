namespace FVMeatbag.Graph.Tests;

public class MaybeTests
{
    [Fact]
    public void Default_is_none_and_null_references_become_none()
    {
        Assert.True(default(Maybe<int>).IsNone);
        Assert.True(Maybe.Some<string?>(null).IsNone);
        Maybe<string> fromNull = (string)null!;
        Assert.True(fromNull.IsNone);
        Assert.True(Maybe.From<int>(null).IsNone);
        Assert.Equal(Maybe.Some(3), Maybe.From<int>(3));
    }

    [Fact]
    public void Value_access_and_fallbacks()
    {
        var some = Maybe.Some(4);
        var none = Maybe<int>.None;

        Assert.Equal(4, some.Value);
        Assert.Throws<InvalidOperationException>(() => none.Value);
        Assert.Equal(9, none.OrDefault(9));
        Assert.Equal(9, none.OrElse(() => 9));
        Assert.Equal("missing", Assert.Throws<InvalidOperationException>(() => none.OrThrow("missing")).Message);
        Assert.True(some.TryGetValue(out var value));
        Assert.Equal(4, value);
        Assert.False(none.TryGetValue(out _));
    }

    [Fact]
    public void Map_bind_where_and_match_skip_none()
    {
        var some = Maybe.Some(4);

        Assert.Equal(Maybe.Some("4"), some.Map(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(Maybe.Some(8), some.Bind(v => Maybe.Some(v * 2)));
        Assert.True(some.Where(v => v > 10).IsNone);
        Assert.True(Maybe<int>.None.Map(v => v + 1).IsNone);
        Assert.Equal("none", Maybe<int>.None.Match(v => "some", () => "none"));
        Assert.Equal("Some(4)", some.ToString());
        Assert.Equal("None", Maybe<int>.None.ToString());
    }

    [Fact]
    public void Equality_compares_presence_then_value()
    {
        Assert.Equal(Maybe<int>.None, default);
        Assert.Equal(Maybe.Some(0), Maybe.Some(0));
        Assert.NotEqual(Maybe.Some(0), Maybe<int>.None); // Some(default) is not None
        Assert.True(Maybe.Some("a") != Maybe.Some("b"));
    }

    [Fact]
    public void Maybe_ports_only_propagate_real_changes()
    {
        var graph = Graph.CreateRoot();
        var source = graph.AddNode(new MaybePassOperation());
        var sinkOp = new MaybePassOperation();
        var sink = graph.AddNode(sinkOp);
        graph.Connect(source.Output("Value"), sink.Input("Value"));
        var before = sinkOp.Executions;

        source.SetInputs(new MaybeInput { Value = Maybe<string>.None });
        Assert.Equal(before, sinkOp.Executions);

        source.SetInputs(new MaybeInput { Value = "mesh" });
        Assert.Equal(Maybe.Some("mesh"), sink.OutputValues.Value);
        Assert.Equal(Maybe.Some("mesh"), sink.Output("Value").GetValue<Maybe<string>>());
    }

    [Fact]
    public void Port_lookups_return_none_for_unknown_names()
    {
        var node = new Node<BinaryInput, ValueOutput>(new AddOperation());

        Assert.Equal(Maybe.Some(node.Inputs[0]), node.FindInput("A"));
        Assert.True(node.FindInput("Nope").IsNone);
        Assert.True(node.FindExecIn("Execute").IsNone);
        Assert.Throws<KeyNotFoundException>(() => node.Output("Nope"));
    }

    public struct MaybeInput
    {
        public Maybe<string> Value;
    }

    public struct MaybeOutput
    {
        public Maybe<string> Value;
    }

    private sealed class MaybePassOperation : CountingOperation<MaybeInput, MaybeOutput>
    {
        protected override void Run(in MaybeInput input, ref MaybeOutput output) => output.Value = input.Value;
    }
}
