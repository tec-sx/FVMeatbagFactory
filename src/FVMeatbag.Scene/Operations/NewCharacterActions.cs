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

/// <summary>Reads a mesh from a glTF file (.glb or .gltf).</summary>
public sealed class LoadMeshAction : ActionOperation<LoadMeshInput, LoadMeshOutput>
{
    public override void Execute(in LoadMeshInput input, ref LoadMeshOutput output, EvaluationContext context) =>
        output.Mesh = GltfImporter.Load(input.Path.OrThrow("No mesh path given."));
}

public struct CreateProjectInput
{
    public Maybe<string> Name;

    /// <summary>Names the project when <see cref="Name"/> is None.</summary>
    public Maybe<LoadedMesh> Mesh;

    /// <summary>Where to put the project folder. None keeps the project in memory only.</summary>
    public Maybe<ProjectWorkspace> Workspace;
}

public struct CreateProjectOutput
{
    public Maybe<CharacterProject> Project;
}

/// <summary>Creates an empty project hierarchy, in its own folder when a workspace is given.</summary>
public sealed class CreateProjectAction : ActionOperation<CreateProjectInput, CreateProjectOutput>
{
    public override void Execute(in CreateProjectInput input, ref CreateProjectOutput output, EvaluationContext context)
    {
        var mesh = input.Mesh;
        var name = input.Name.OrElse(() => mesh.Map(m => m.Name).OrThrow("No project name or mesh to name it after."));
        var directory = input.Workspace.Map(w => w.NewProjectDirectory(name));
        output.Project = new CharacterProject(name, directory);
    }
}

public struct AddMeshToProjectInput
{
    public Maybe<CharacterProject> Project;
    public Maybe<LoadedMesh> Mesh;
}

public struct AddMeshToProjectOutput
{
    public Maybe<CharacterProject> Project;
    public Maybe<SkeletalMeshAsset> Asset;
}

/// <summary>
/// Adds a loaded mesh to the project's Meshes folder. The source file is copied into the project
/// folder so the project stays self-contained.
/// </summary>
public sealed class AddMeshToProjectAction : ActionOperation<AddMeshToProjectInput, AddMeshToProjectOutput>
{
    public override void Execute(in AddMeshToProjectInput input, ref AddMeshToProjectOutput output, EvaluationContext context)
    {
        var project = input.Project.OrThrow("No project open.");
        var mesh = input.Mesh.OrThrow("No mesh loaded.");
        var asset = new SkeletalMeshAsset(mesh.Name, ProjectFile.CopyIntoProject(project, mesh.SourcePath))
        {
            Geometry = mesh.Geometry,
        };
        output.Asset = project.Meshes.Add(asset);
        output.Project = project;
    }
}

public struct AddToSceneInput
{
    public Maybe<SceneRoot> Scene;
    public Maybe<CharacterProject> Project;
    public Maybe<SkeletalMeshAsset> Asset;
}

public struct AddToSceneOutput
{
    public Maybe<MeshComponent> Component;
}

/// <summary>
/// Shows a project asset in the scene, centered on the origin. If the scene is showing another
/// project, it switches to this one first.
/// </summary>
public sealed class AddToSceneAction : ActionOperation<AddToSceneInput, AddToSceneOutput>
{
    public override void Execute(in AddToSceneInput input, ref AddToSceneOutput output, EvaluationContext context)
    {
        var scene = input.Scene.OrThrow("No scene.");
        var asset = input.Asset.OrThrow("No asset to add.");
        var project = input.Project.OrThrow("No project.");
        if (scene.Project != project)
        {
            scene.Show(project);
            output.Component = scene.FindMesh(asset.Id);
        }
        else
        {
            output.Component = scene.FindMesh(asset.Id).OrElse(() => scene.AddMesh(asset));
        }
        scene.FrameAll();
    }
}

public struct SaveProjectInput
{
    public Maybe<CharacterProject> Project;
    public Maybe<SceneRoot> Scene;
}

public struct SaveProjectOutput
{
    public Maybe<string> Path;
}

/// <summary>Writes the project file of a project that has a folder, taking the camera and light from the scene when it shows this project.</summary>
public sealed class SaveProjectAction : ActionOperation<SaveProjectInput, SaveProjectOutput>
{
    public override void Execute(in SaveProjectInput input, ref SaveProjectOutput output, EvaluationContext context)
    {
        var project = input.Project.OrThrow("No project to save.");
        if (project.Directory.IsNone)
        {
            output.Path = Maybe<string>.None; // in-memory project, nothing to write
            return;
        }
        if (input.Scene.Where(s => s.Project == project).TryGetValue(out var scene))
        {
            scene.StoreViewSettings(project);
        }
        ProjectFile.Save(project);
        output.Path = project.Directory.Map(d => Path.Combine(d, ProjectFile.FileName));
    }
}
