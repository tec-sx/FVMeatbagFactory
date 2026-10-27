using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FVMeatbag.Core.Project;
using FVMeatbag.Graph;
using FVMeatbag.Rendering;
using FVMeatbag.Scene;
using FVMeatbag.Scene.Components;
using FVMeatbag.Scene.Tools;

namespace FVMeatbag.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly List<SkeletonComponent> _watchedSkeletons = [];
    private string _message = "";
    private TransformTool _shownTool;

    [ObservableProperty]
    private string _status = "No project";

    [ObservableProperty]
    private SceneComponentViewModel? _selectedComponent;

    [ObservableProperty]
    private DetailsViewModel? _details;

    /// <summary>How the viewport draws meshes; set from the top bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWireframe), nameof(IsSolid), nameof(IsSolidWireframe), nameof(IsShaded))]
    private MeshDisplayMode _displayMode = MeshDisplayMode.Solid;

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
        Host.Scene.Manipulator.Changed += (_, _) => OnToolChanged();

        var problems = Host.OpenStartupProject();
        Report(problems.Count == 0 ? "Ready. File → Import Mesh… to load a .glb or .fbx." : string.Join(" ", problems));
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

    // One per top bar button; they act as a radio group, so unchecking is ignored.
    public bool IsWireframe
    {
        get => DisplayMode == MeshDisplayMode.Wireframe;
        set => SetDisplayMode(MeshDisplayMode.Wireframe, value);
    }

    public bool IsSolid
    {
        get => DisplayMode == MeshDisplayMode.Solid;
        set => SetDisplayMode(MeshDisplayMode.Solid, value);
    }

    public bool IsSolidWireframe
    {
        get => DisplayMode == MeshDisplayMode.SolidWireframe;
        set => SetDisplayMode(MeshDisplayMode.SolidWireframe, value);
    }

    /// <summary>Placeholder until materials exist; draws like solid.</summary>
    public bool IsShaded
    {
        get => DisplayMode == MeshDisplayMode.Shaded;
        set => SetDisplayMode(MeshDisplayMode.Shaded, value);
    }

    /// <summary>The viewport tool; the overlay buttons and Q, W, E, R set it.</summary>
    public TransformTool Tool
    {
        get => Host.Scene.Manipulator.Tool;
        set => Host.Scene.Manipulator.Tool = value;
    }

    // One per tool button; like the display modes they act as a radio group.
    public bool IsSelectTool
    {
        get => Tool == TransformTool.Select;
        set => SetTool(TransformTool.Select, value);
    }

    public bool IsMoveTool
    {
        get => Tool == TransformTool.Move;
        set => SetTool(TransformTool.Move, value);
    }

    public bool IsRotateTool
    {
        get => Tool == TransformTool.Rotate;
        set => SetTool(TransformTool.Rotate, value);
    }

    public bool IsScaleTool
    {
        get => Tool == TransformTool.Scale;
        set => SetTool(TransformTool.Scale, value);
    }

    /// <summary>Shows or hides every skeleton's bones.</summary>
    public bool ShowBones
    {
        get => Host.Scene.ShowBones;
        set
        {
            Host.Scene.ShowBones = value;
            OnPropertyChanged();
            Details?.Refresh();
        }
    }

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

    /// <summary>Keys 4 to 7, as in Maya.</summary>
    [RelayCommand]
    private void SetDisplayMode(MeshDisplayMode mode) => DisplayMode = mode;

    /// <summary>Keys Q, W, E, R, as in Maya.</summary>
    [RelayCommand]
    private void SelectTool(TransformTool tool) => Tool = tool;

    private void SetTool(TransformTool tool, bool on)
    {
        if (on)
        {
            Tool = tool;
        }
    }

    private void OnToolChanged()
    {
        if (_shownTool == Tool)
        {
            return;
        }
        _shownTool = Tool;
        OnPropertyChanged(nameof(Tool));
        OnPropertyChanged(nameof(IsSelectTool));
        OnPropertyChanged(nameof(IsMoveTool));
        OnPropertyChanged(nameof(IsRotateTool));
        OnPropertyChanged(nameof(IsScaleTool));
        var hint = Tool switch
        {
            TransformTool.Move => "Move: drag an arrow, or the square to move in the view plane.",
            TransformTool.Rotate => "Rotate: drag a ring, or the outer ring to turn about the view.",
            TransformTool.Scale => "Scale: drag an axis, or the center cube to scale evenly.",
            _ => "Select: click a joint or bone.",
        };
        Report(Host.Scene.Manipulator.Target.HasValue || Tool == TransformTool.Select ? hint : $"{hint} Select a bone first.");
    }

    private void SetDisplayMode(MeshDisplayMode mode, bool on)
    {
        if (on)
        {
            DisplayMode = mode;
        }
    }

    partial void OnDetailsChanged(DetailsViewModel? oldValue, DetailsViewModel? newValue) => (oldValue as IDisposable)?.Dispose();

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
        WatchSkeletons();
        UpdateStatus();
    }

    private void WatchSkeletons()
    {
        foreach (var skeleton in _watchedSkeletons)
        {
            skeleton.SelectedBoneChanged -= OnBoneSelected;
        }
        _watchedSkeletons.Clear();
        _watchedSkeletons.AddRange(Host.Scene.Skeletons);
        foreach (var skeleton in _watchedSkeletons)
        {
            skeleton.SelectedBoneChanged += OnBoneSelected;
        }
    }

    /// <summary>A bone picked in the viewport brings its skeleton into the details panel, where the tree shows it.</summary>
    private void OnBoneSelected(object? sender, EventArgs e)
    {
        if (sender is not SkeletonComponent skeleton)
        {
            return;
        }
        if (!skeleton.SelectedBone.TryGetValue(out var bone))
        {
            if (Host.Scene.Skeletons.All(s => s.SelectedBone.IsNone))
            {
                Report("No bone selected.");
            }
            return;
        }
        if (SelectedComponent?.Component != skeleton)
        {
            SelectedComponent = Find(Outliner, skeleton);
        }
        Report($"Selected bone {skeleton.Skeleton.Bones[bone].Name}.");

        static SceneComponentViewModel? Find(IEnumerable<SceneComponentViewModel> items, SceneComponent component) =>
            items.Select(i => i.Component == component ? i : Find(i.Children, component)).FirstOrDefault(i => i is not null);
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
