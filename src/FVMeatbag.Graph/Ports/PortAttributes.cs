namespace FVMeatbag.Graph.Ports;

/// <summary>Overrides the display name of a port. Optional: every public instance field is a port by default.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class PortAttribute(string displayName) : Attribute
{
    public string DisplayName { get; } = displayName;
}

/// <summary>Excludes a public field of an input/output struct from becoming a port.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class PortIgnoreAttribute : Attribute;
