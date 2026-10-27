using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FVMeatbag.Core.Project;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Components;

namespace FVMeatbag.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private string _message = "";

    [ObservableProperty]
    private string _status = "No project";

    [ObservableProperty]
    private SceneComponentViewModel? _selectedComponent;

    [ObservableProperty]
    private DetailsViewModel? _details;

    public MainWindowViewModel(AppHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Host = host;
        Host.Scene.ProjectChanged += (_, _) => RefreshOutliner();
        Host.NewCharacter.AddToScene.OutputsChanged += (_, _) => RefreshOutliner();
        Host.ImportMesh.AddToScene.OutputsChanged += (_, _) =>
        {
            RefreshOutliner();
            Report(Host.ImportMesh.LoadMesh.OutputValues.Mesh.Map(m => $"Imported {m.Name}. Save (Ctrl+S) to keep it.").OrDefault(""));
        };
        Host.NewCharacter.Save.OutputsChanged += (_, _) => Report("New project created and saved.");
        Host.RootGraph.Evaluator.NodeFaulted += (_, e) => Report($"{e.Node.Name} failed: {e.Exception.Message}");
        Host.Scene.Camera.CameraNode!.OutputsChanged += (_, _) => (Details as CameraDetailsViewModel)?.Refresh();
        Host.Scene.Light.LightNode!.OutputsChanged += (_, _) => (Details as LightDetailsViewModel)?.Refresh();

        var problems = Host.OpenStartupProject();
        Report(problems.Count == 0 ? "Ready. File → Import Mesh… to load a .glb." : string.Join(" ", problems));
    }

    /// <summary>Design-time constructor.</summary>
    public MainWindowViewModel() : this(new AppHost())
    {
    }

    public AppHost Host { get; }

    /// <summary>Set by the window.</summary>
    public IFileDialogs? Dialogs { get; set; }

    public ObservableCollection<SceneComponentViewModel> Outliner { get; } = [];

    private Maybe<CharacterProject> Project => Host.Scene.Project;

    [RelayCommand]
    private async Task NewProject()
    {
        if (Dialogs is null || await Dialogs.PickMeshFileAsync() is not { } path)
        {
            return;
        }
        // Load mesh → create project → add mesh → add to scene → save; the project is named after the mesh.
        Host.NewCharacter.Run(path, workspace: Host.Workspace);
    }

    [RelayCommand]
    private async Task OpenProject()
    {
        if (Dialogs is null || await Dialogs.PickProjectFolderAsync(Host.Workspace.Root) is not { } folder)
        {
            return;
        }
        if (!ProjectFile.Exists(folder))
        {
            Report($"{folder} has no {ProjectFile.FileName}.");
            return;
        }
        try
        {
            var problems = Host.OpenProject(folder);
            Report(problems.Count == 0 ? "Project opened." : string.Join(" ", problems));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            Report($"Could not open the project: {e.Message}");
        }
    }

    [RelayCommand]
    private async Task ImportMesh()
    {
        if (!Project.TryGetValue(out var project))
        {
            Report("Open or create a project first.");
            return;
        }
        if (Dialogs is null || await Dialogs.PickMeshFileAsync() is not { } path)
        {
            return;
        }
        // Load mesh → add mesh to project → add to scene (centered, camera framed).
        Host.ImportMesh.Run(path, project);
    }

    /// <summary>Imports a mesh into the open project without asking, e.g. from the command line.</summary>
    public void ImportMeshFile(string path)
    {
        if (Project.TryGetValue(out var project))
        {
            Host.ImportMesh.Run(Path.GetFullPath(path), project);
        }
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            Report(Host.SaveProject().Match(path => $"Saved {path}", () => "This project lives in memory only."));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Report($"Could not save: {e.Message}");
        }
    }

    [RelayCommand]
    private void FrameAll() => Host.Scene.FrameAll();

    [RelayCommand]
    private void ResetCamera() => Host.Scene.Camera.Settings = Project.Map(p => p.Camera).OrDefault(OrbitCameraSettings.Default);

    partial void OnSelectedComponentChanged(SceneComponentViewModel? value)
    {
        var component = value?.Component;
        // Gizmos are for things that move; the camera and light are edited in the details panel.
        Host.Scene.Select(component is MeshComponent ? component : null);
        Details = component switch
        {
            OrbitCameraComponent camera => new CameraDetailsViewModel(camera),
            DirectionalLightComponent light => new LightDetailsViewModel(light),
            MeshComponent mesh => new MeshDetailsViewModel(Host.Scene, mesh),
            SkeletonComponent skeleton => new SkeletonDetailsViewModel(skeleton),
            _ => null,
        };
    }

    private void RefreshOutliner()
    {
        Outliner.Clear();
        Outliner.Add(new SceneComponentViewModel(Host.Scene.Root));
        UpdateStatus();
    }

    private void Report(string message)
    {
        _message = message;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var project = Project.Map(p => p.Directory.Match(d => $"{p.Name} ({d})", () => p.Name)).OrDefault("No project");
        Status = string.IsNullOrEmpty(_message) ? project : $"{project}  |  {_message}";
    }
}
