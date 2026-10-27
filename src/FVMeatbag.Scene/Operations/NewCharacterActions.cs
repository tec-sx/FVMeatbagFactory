using FVMeatbag.Core.Import;
using FVMeatbag.Core.Project;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Components;

namespace FVMeatbag.Scene.Operations;

public struct LoadMeshInput
{
    public Maybe<string> Path;
}

public struct LoadMeshOutput
{
    public Maybe<LoadedMesh> Mesh;
}

/// <summary>Reads a base skeletal mesh from disk. Placeholder: only records name and path for now.</summary>
public sealed class LoadMeshAction : ActionOperation<LoadMeshInput, LoadMeshOutput>
{
    public override void Execute(in LoadMeshInput input, ref LoadMeshOutput output, EvaluationContext context)
    {
        var path = input.Path.OrThrow("No mesh path given.");
        output.Mesh = new LoadedMesh(Path.GetFileNameWithoutExtension(path), path);
    }
}

public struct CreateProjectInput
{
    public Maybe<string> Name;
    public Maybe<LoadedMesh> Mesh;
}

public struct CreateProjectOutput
{
    public Maybe<CharacterProject> Project;
    public Maybe<SkeletalMeshAsset> BaseMesh;
}

/// <summary>Builds a new project hierarchy around a loaded base mesh.</summary>
public sealed class CreateProjectAction : ActionOperation<CreateProjectInput, CreateProjectOutput>
{
    public override void Execute(in CreateProjectInput input, ref CreateProjectOutput output, EvaluationContext context)
    {
        var mesh = input.Mesh.OrThrow("No mesh loaded.");
        var project = new CharacterProject(input.Name.OrDefault(mesh.Name));
        output.BaseMesh = project.Meshes.Add(new SkeletalMeshAsset(mesh.Name, mesh.SourcePath));
        output.Project = project;
    }
}

public struct AddToSceneInput
{
    public Maybe<SceneRoot> Scene;
    public Maybe<SkeletalMeshAsset> Asset;
}

public struct AddToSceneOutput
{
    public Maybe<MeshComponent> Component;
}

/// <summary>Creates a mesh scene component that displays a project asset.</summary>
public sealed class AddToSceneAction : ActionOperation<AddToSceneInput, AddToSceneOutput>
{
    public override void Execute(in AddToSceneInput input, ref AddToSceneOutput output, EvaluationContext context)
    {
        var scene = input.Scene.OrThrow("No scene.");
        var asset = input.Asset.OrThrow("No asset to add.");
        output.Component = scene.Root.AddChild(new MeshComponent(asset.Name, asset.Id));
    }
}
