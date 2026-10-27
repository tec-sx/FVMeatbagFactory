using FVMeatbag.Core.Import;
using FVMeatbag.Core.Project;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Components;

namespace FVMeatbag.Scene.Operations;

public struct LoadMeshInput
{
    public string? Path;
}

public struct LoadMeshOutput
{
    public LoadedMesh? Mesh;
}

/// <summary>Reads a base skeletal mesh from disk. Placeholder: only records name and path for now.</summary>
public sealed class LoadMeshAction : ActionOperation<LoadMeshInput, LoadMeshOutput>
{
    public override void Execute(in LoadMeshInput input, ref LoadMeshOutput output, EvaluationContext context)
    {
        var path = input.Path ?? throw new InvalidOperationException("No mesh path given.");
        output.Mesh = new LoadedMesh(Path.GetFileNameWithoutExtension(path), path);
    }
}

public struct CreateProjectInput
{
    public string? Name;
    public LoadedMesh? Mesh;
}

public struct CreateProjectOutput
{
    public CharacterProject? Project;
    public SkeletalMeshAsset? BaseMesh;
}

/// <summary>Builds a new project hierarchy around a loaded base mesh.</summary>
public sealed class CreateProjectAction : ActionOperation<CreateProjectInput, CreateProjectOutput>
{
    public override void Execute(in CreateProjectInput input, ref CreateProjectOutput output, EvaluationContext context)
    {
        var mesh = input.Mesh ?? throw new InvalidOperationException("No mesh loaded.");
        var project = new CharacterProject(input.Name ?? mesh.Name);
        output.BaseMesh = project.Meshes.Add(new SkeletalMeshAsset(mesh.Name, mesh.SourcePath));
        output.Project = project;
    }
}

public struct AddToSceneInput
{
    public SceneRoot? Scene;
    public SkeletalMeshAsset? Asset;
}

public struct AddToSceneOutput
{
    public MeshComponent? Component;
}

/// <summary>Creates a mesh scene component that displays a project asset.</summary>
public sealed class AddToSceneAction : ActionOperation<AddToSceneInput, AddToSceneOutput>
{
    public override void Execute(in AddToSceneInput input, ref AddToSceneOutput output, EvaluationContext context)
    {
        var scene = input.Scene ?? throw new InvalidOperationException("No scene.");
        var asset = input.Asset ?? throw new InvalidOperationException("No asset to add.");
        output.Component = scene.Root.AddChild(new MeshComponent(asset.Name, asset.Id));
    }
}
