using FVMeatbag.Core.Project;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.Scene;

/// <summary>
/// Load mesh → create project → add mesh to project → add to scene → save, built from action nodes
/// joined by exec wires, with the mesh, project and asset passed along data wires.
/// </summary>
public sealed class NewCharacterWorkflow
{
    public NewCharacterWorkflow(Graph.Graph graph, SceneRoot scene)
    {
        LoadMesh = graph.AddNode(new LoadMeshAction());
        CreateProject = graph.AddNode(new CreateProjectAction());
        AddMesh = graph.AddNode(new AddMeshToProjectAction());
        AddToScene = graph.AddNode(new AddToSceneAction(), initialInput: new AddToSceneInput { Scene = scene });
        Save = graph.AddNode(new SaveProjectAction(), initialInput: new SaveProjectInput { Scene = scene });

        graph.Connect(LoadMesh.ExecOut(), CreateProject.ExecIn());
        graph.Connect(CreateProject.ExecOut(), AddMesh.ExecIn());
        graph.Connect(AddMesh.ExecOut(), AddToScene.ExecIn());
        graph.Connect(LoadMesh.Output(nameof(LoadMeshOutput.Mesh)), CreateProject.Input(nameof(CreateProjectInput.Mesh)));
        graph.Connect(LoadMesh.Output(nameof(LoadMeshOutput.Mesh)), AddMesh.Input(nameof(AddMeshToProjectInput.Mesh)));
        graph.Connect(CreateProject.Output(nameof(CreateProjectOutput.Project)), AddMesh.Input(nameof(AddMeshToProjectInput.Project)));
        graph.Connect(AddMesh.Output(nameof(AddMeshToProjectOutput.Project)), AddToScene.Input(nameof(AddToSceneInput.Project)));
        graph.Connect(AddMesh.Output(nameof(AddMeshToProjectOutput.Asset)), AddToScene.Input(nameof(AddToSceneInput.Asset)));

        // Only projects that have a folder are saved; in-memory projects end after Add to scene.
        graph.Connect(AddToScene.ExecOut(), Save.ExecIn());
        graph.Connect(CreateProject.Output(nameof(CreateProjectOutput.Project)), Save.Input(nameof(SaveProjectInput.Project)));
    }

    public Node<LoadMeshInput, LoadMeshOutput> LoadMesh { get; }
    public Node<CreateProjectInput, CreateProjectOutput> CreateProject { get; }
    public Node<AddMeshToProjectInput, AddMeshToProjectOutput> AddMesh { get; }
    public Node<AddToSceneInput, AddToSceneOutput> AddToScene { get; }
    public Node<SaveProjectInput, SaveProjectOutput> Save { get; }

    /// <summary>Starts the chain. In deferred mode it runs on the next evaluate.</summary>
    public void Run(string meshPath, Maybe<string> projectName = default, Maybe<ProjectWorkspace> workspace = default)
    {
        LoadMesh.SetInputs(new LoadMeshInput { Path = meshPath });
        CreateProject.UpdateInputs((ref CreateProjectInput i) =>
        {
            i.Name = projectName;
            i.Workspace = workspace;
        });
        LoadMesh.Trigger();
    }
}
