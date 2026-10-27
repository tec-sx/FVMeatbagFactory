using Avalonia.Threading;
using FVMeatbag.Core.Project;
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
        var workflows = RootGraph.CreateChild("Workflows");
        NewCharacter = new NewCharacterWorkflow(workflows.CreateChild("NewCharacter"), Scene);
        ImportMesh = new ImportMeshWorkflow(workflows.CreateChild("ImportMesh"), Scene);
        Renderer = new VulkanBackend();
        Workspace = ProjectWorkspace.ForCurrentUser();

        // Edits only queue work; the graph settles once per frame, however many edits arrived.
        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
        _tick.Start();
    }

    public Graph.Graph RootGraph { get; }
    public SceneRoot Scene { get; }
    public NewCharacterWorkflow NewCharacter { get; }
    public ImportMeshWorkflow ImportMesh { get; }
    public IRenderBackend Renderer { get; }

    /// <summary>Documents/Projects.</summary>
    public ProjectWorkspace Workspace { get; }

    /// <summary>Raised after a frame in which at least one node ran.</summary>
    public event EventHandler<int>? GraphEvaluated;

    /// <summary>
    /// First-run setup, like an installer: creates Documents/Projects and the Demo project if they
    /// are missing, then opens Demo. Returns problems worth showing the user.
    /// </summary>
    public IReadOnlyList<string> OpenStartupProject()
    {
        try
        {
            return OpenProject(Workspace.EnsureCreated());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            Scene.Show(new CharacterProject(ProjectWorkspace.DemoProjectName));
            return [$"Could not set up {Workspace.DemoDirectory}: {e.Message}. Working in memory."];
        }
    }

    public IReadOnlyList<string> OpenProject(string directory)
    {
        var problems = new List<string>();
        Scene.Show(ProjectFile.Load(directory, problems));
        return problems;
    }

    /// <summary>Saves the open project, with the current camera and light. Returns the file written, or None for an in-memory project.</summary>
    public Maybe<string> SaveProject()
    {
        var project = Scene.Project.OrThrow("No project is open.");
        if (project.Directory.IsNone)
        {
            return Maybe<string>.None;
        }
        Scene.StoreViewSettings(project);
        ProjectFile.Save(project);
        return project.Directory.Map(d => Path.Combine(d, ProjectFile.FileName));
    }

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
