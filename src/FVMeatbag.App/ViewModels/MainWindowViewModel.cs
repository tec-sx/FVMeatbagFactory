using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FVMeatbag.Core.Project;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Components;

namespace FVMeatbag.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppHost _host;
    private Maybe<CharacterProject> _project;

    [ObservableProperty]
    private string _status = "No project";

    [ObservableProperty]
    private SceneComponentViewModel? _selectedComponent;

    public MainWindowViewModel(AppHost host)
    {
        _host = host;
        _host.GraphEvaluated += (_, count) => Status = $"{ProjectLabel} | graphs: {GraphCount} | last frame evaluated {count} node(s)";
        _host.NewCharacter.AddToScene.OutputsChanged += (_, _) =>
        {
            _project = _host.NewCharacter.CreateProject.OutputValues.Project;
            RefreshOutliner();
        };
        RefreshOutliner();
    }

    /// <summary>Design-time constructor.</summary>
    public MainWindowViewModel() : this(new AppHost())
    {
    }

    public ObservableCollection<SceneComponentViewModel> Outliner { get; } = [];

    private string ProjectLabel => _project.Map(p => p.Name).OrDefault("No project");
    private int GraphCount => _host.RootGraph.DescendantsAndSelf().Count();

    // Runs load mesh → create project → add to scene. The path is a placeholder until there is a file dialog.
    [RelayCommand]
    private void NewProject() =>
        _host.NewCharacter.Run("BaseBody.fbx", $"Character {DateTime.Now:HHmmss}");

    partial void OnSelectedComponentChanged(SceneComponentViewModel? value) =>
        _host.Scene.Select(value?.Component is GizmoComponent or null ? null : value.Component);

    private void RefreshOutliner()
    {
        Outliner.Clear();
        Outliner.Add(new SceneComponentViewModel(_host.Scene.Root));
        Status = $"{ProjectLabel} | graphs: {GraphCount}";
    }
}
