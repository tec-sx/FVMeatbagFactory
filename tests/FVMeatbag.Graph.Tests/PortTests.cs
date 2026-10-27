using FVMeatbag.Graph.Ports;

namespace FVMeatbag.Graph.Tests;

public class PortTests
{
    [Fact]
    public void Public_fields_become_ports_in_declaration_order()
    {
        var node = new Node<BinaryInput, ValueOutput>(new AddOperation());

        Assert.Equal(["A", "B"], node.Inputs.Select(p => p.Name));
        Assert.Equal(["Value"], node.Outputs.Select(p => p.Name));
        Assert.All(node.Inputs, p => Assert.Equal(typeof(float), p.ValueType));
    }

    [Fact]
    public void Attributes_rename_and_exclude_ports()
    {
        var node = new Node<AnnotatedInput, LabelOutput>(new LabelOperation());

        Assert.Equal(["Radius", "Label"], node.Inputs.Select(p => p.Name));
        Assert.Equal("Brush Radius", node.Input("Radius").DisplayName);
    }

    [Fact]
    public void Node_name_defaults_to_operation_name_without_suffix()
    {
        Assert.Equal("Add", new Node<BinaryInput, ValueOutput>(new AddOperation()).Name);
    }

    [Fact]
    public void Port_values_are_read_and_written_by_name()
    {
        var graph = Graph.CreateRoot();
        var add = graph.AddNode(new AddOperation());

        add.Input("A").SetValue(2f);
        add.Input("B").SetValue(3f);

        Assert.Equal(2f, add.Input("A").GetValue<float>());
        Assert.Equal(5f, add.Output("Value").GetValue<float>());
    }

    [Fact]
    public void Null_reference_values_round_trip()
    {
        var graph = Graph.CreateRoot();
        var node = graph.AddNode(new LabelOperation());

        node.Input("Label").SetValue("hand");
        node.Input("Label").SetValue<string?>(null);

        Assert.Null(node.Output("Label").GetValue<string?>());
    }

    [Fact]
    public void Setting_a_value_of_the_wrong_type_throws()
    {
        var graph = Graph.CreateRoot();
        var add = graph.AddNode(new AddOperation());

        Assert.Throws<InvalidCastException>(() => add.Input("A").SetValue("nope"));
    }
}
