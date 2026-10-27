using FVMeatbag.Core.Project;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.Scene;

/// <summary>
/// Load mesh → add mesh to project → add to scene: brings a glTF file into the open project and
/// places it at the center of the scene. Saving is left to the user.
/// </summary>
public sealed class ImportMeshWorkflow
{
    public ImportMeshWorkflow(Graph.Graph graph, SceneRoot scene)
    {
        LoadMesh = graph.AddNode(new LoadMeshAction());
        AddMesh = graph.AddNode(new AddMeshToProjectAction());
        AddToScene = graph.AddNode(new AddToSceneAction(), initialInput: new AddToSceneInput { Scene = scene });

        graph.Connect(LoadMesh.ExecOut(), AddMesh.ExecIn());
        graph.Connect(AddMesh.ExecOut(), AddToScene.ExecIn());
        graph.Connect(LoadMesh.Output(nameof(LoadMeshOutput.Mesh)), AddMesh.Input(nameof(AddMeshToProjectInput.Mesh)));
        graph.Connect(AddMesh.Output(nameof(AddMeshToProjectOutput.Project)), AddToScene.Input(nameof(AddToSceneInput.Project)));
        graph.Connect(AddMesh.Output(nameof(AddMeshToProjectOutput.Asset)), AddToScene.Input(nameof(AddToSceneInput.Asset)));
    }

    public Node<LoadMeshInput, LoadMeshOutput> LoadMesh { get; }
    public Node<AddMeshToProjectInput, AddMeshToProjectOutput> AddMesh { get; }
    public Node<AddToSceneInput, AddToSceneOutput> AddToScene { get; }

    /// <summary>Starts the chain. In deferred mode it runs on the next evaluate.</summary>
    public void Run(string meshPath, CharacterProject project)
    {
        LoadMesh.SetInputs(new LoadMeshInput { Path = meshPath });
        AddMesh.UpdateInputs((ref AddMeshToProjectInput i) => i.Project = project);
        LoadMesh.Trigger();
    }
}
