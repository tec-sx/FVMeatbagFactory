using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FVMeatbag.Core.Project;
using FVMeatbag.Scene.Components;

namespace FVMeatbag.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppHost _host;
    private CharacterProject? _project;

    [ObservableProperty]
    private string _status = "No project";

    [ObservableProperty]
    private SceneComponentViewModel? _selectedComponent;

    public MainWindowViewModel(AppHost host)
    {
        _host = host;
        _host.GraphEvaluated += (_, count) => Status = $"{ProjectLabel} | graphs: {GraphCount} | last frame evaluated {count} node(s)";
        RefreshOutliner();
    }

    /// <summary>Design-time constructor.</summary>
    public MainWindowViewModel() : this(new AppHost())
    {
    }

    public ObservableCollection<SceneComponentViewModel> Outliner { get; } = [];

    private string ProjectLabel => _project?.Name ?? "No project";
    private int GraphCount => _host.RootGraph.DescendantsAndSelf().Count();

    // Placeholder until the "create project" / "load mesh" flow is built from nodes.
    [RelayCommand]
    private void NewProject()
    {
        _project = new CharacterProject($"Character {DateTime.Now:HHmmss}");
        var asset = _project.Meshes.Add(new SkeletalMeshAsset("BaseBody"));
        _host.Scene.Root.AddChild(new MeshComponent(asset.Name, asset.Id));
        RefreshOutliner();
    }

    partial void OnSelectedComponentChanged(SceneComponentViewModel? value) =>
        _host.Scene.Select(value?.Component is GizmoComponent or null ? null : value.Component);

    private void RefreshOutliner()
    {
        Outliner.Clear();
        Outliner.Add(new SceneComponentViewModel(_host.Scene.Root));
        Status = $"{ProjectLabel} | graphs: {GraphCount}";
    }
}
