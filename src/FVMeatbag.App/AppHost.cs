using Avalonia.Threading;
using FVMeatbag.Graph;
using FVMeatbag.Rendering;
using FVMeatbag.Rendering.Vulkan;
using FVMeatbag.Scene;

namespace FVMeatbag.App;

/// <summary>
/// Owns the application's root graph, the root scene and the render backend, and drives graph
/// evaluation once per UI frame.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly DispatcherTimer _tick;

    public AppHost()
    {
        RootGraph = Graph.Graph.CreateRoot("App", EvaluationMode.Deferred);
        Scene = new SceneRoot(RootGraph);
        Renderer = new VulkanBackend();

        // Edits only queue work; the graph settles once per frame, however many edits arrived.
        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
        _tick.Start();
    }

    public Graph.Graph RootGraph { get; }
    public SceneRoot Scene { get; }
    public IRenderBackend Renderer { get; }

    /// <summary>Raised after a frame in which at least one node ran.</summary>
    public event EventHandler<int>? GraphEvaluated;

    public void Dispose()
    {
        _tick.Stop();
        Renderer.Dispose();
    }

    private void Tick()
    {
        if (!RootGraph.Evaluator.HasPendingWork)
        {
            return;
        }
        var evaluated = RootGraph.Evaluator.Evaluate();
        GraphEvaluated?.Invoke(this, evaluated);
    }
}
