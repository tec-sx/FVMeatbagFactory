namespace FVMeatbag.Graph;

public class GraphException(string message) : InvalidOperationException(message);

/// <summary>Thrown when a connection would create a cycle.</summary>
public sealed class GraphCycleException(string message) : GraphException(message);
