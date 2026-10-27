using FVMeatbag.Graph;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.Scene;

/// <summary>
/// Load mesh → create project → add to scene, built from three action nodes joined by exec wires,
/// with the mesh, asset and scene passed along data wires.
/// </summary>
public sealed class NewCharacterWorkflow
{
    public NewCharacterWorkflow(Graph.Graph graph, SceneRoot scene)
    {
        LoadMesh = graph.AddNode(new LoadMeshAction());
        CreateProject = graph.AddNode(new CreateProjectAction());
        AddToScene = graph.AddNode(new AddToSceneAction(), initialInput: new AddToSceneInput { Scene = scene });

        graph.Connect(LoadMesh.ExecOut(), CreateProject.ExecIn());
        graph.Connect(CreateProject.ExecOut(), AddToScene.ExecIn());
        graph.Connect(LoadMesh.Output(nameof(LoadMeshOutput.Mesh)), CreateProject.Input(nameof(CreateProjectInput.Mesh)));
        graph.Connect(CreateProject.Output(nameof(CreateProjectOutput.BaseMesh)), AddToScene.Input(nameof(AddToSceneInput.Asset)));
    }

    public Node<LoadMeshInput, LoadMeshOutput> LoadMesh { get; }
    public Node<CreateProjectInput, CreateProjectOutput> CreateProject { get; }
    public Node<AddToSceneInput, AddToSceneOutput> AddToScene { get; }

    /// <summary>Starts the chain. In deferred mode it runs on the next evaluate.</summary>
    public void Run(string meshPath, Maybe<string> projectName = default)
    {
        LoadMesh.SetInputs(new LoadMeshInput { Path = meshPath });
        CreateProject.UpdateInputs((ref CreateProjectInput i) => i.Name = projectName);
        LoadMesh.Trigger();
    }
}
