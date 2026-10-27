using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FVMeatbag.Core.Project;
using FVMeatbag.Graph;
using FVMeatbag.Scene;
using FVMeatbag.Scene.Components;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.App.ViewModels;

/// <summary>Details panel content for the selected outliner entry.</summary>
public abstract class DetailsViewModel : ViewModelBase
{
    public abstract string Title { get; }

    /// <summary>Re-reads the component, e.g. after the viewport moved the camera.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>Edits the scene's one camera. Cameras are adjusted, never added or removed.</summary>
public sealed partial class CameraDetailsViewModel(OrbitCameraComponent camera) : DetailsViewModel
{
    public override string Title => "Camera (orbit)";

    public double Yaw
    {
        get => camera.Settings.YawDegrees;
        set => camera.Settings = camera.Settings with { YawDegrees = (float)value };
    }

    public double Pitch
    {
        get => camera.Settings.PitchDegrees;
        set => camera.Settings = camera.Settings with { PitchDegrees = (float)value };
    }

    public double Distance
    {
        get => camera.Settings.Distance;
        set => camera.Settings = camera.Settings with { Distance = (float)value };
    }

    public double FieldOfView
    {
        get => camera.Settings.FieldOfViewDegrees;
        set => camera.Settings = camera.Settings with { FieldOfViewDegrees = (float)value };
    }

    public string Target => $"{camera.Settings.Target.X:0.###}, {camera.Settings.Target.Y:0.###}, {camera.Settings.Target.Z:0.###}";

    [RelayCommand]
    private void Reset() => camera.Settings = OrbitCameraSettings.Default;
}

/// <summary>Edits the scene's one directional light. Lights are adjusted, never added or removed.</summary>
public sealed partial class LightDetailsViewModel(DirectionalLightComponent light) : DetailsViewModel
{
    public override string Title => "Light (directional)";

    public double Azimuth
    {
        get => light.Settings.AzimuthDegrees;
        set => light.Settings = light.Settings with { AzimuthDegrees = (float)value };
    }

    public double Elevation
    {
        get => light.Settings.ElevationDegrees;
        set => light.Settings = light.Settings with { ElevationDegrees = (float)value };
    }

    public double Intensity
    {
        get => light.Settings.Intensity;
        set => light.Settings = light.Settings with { Intensity = (float)value };
    }

    [RelayCommand]
    private void Reset() => light.Settings = DirectionalLightSettings.Default;
}

/// <summary>Read-only facts about a mesh in the scene.</summary>
public sealed class MeshDetailsViewModel(SceneRoot scene, MeshComponent mesh) : DetailsViewModel
{
    public override string Title => mesh.Name;

    public string Source => scene.FindAsset(mesh).Bind(a => Maybe.From(a.SourcePath)).OrDefault("(none)");

    public string Geometry => scene.FindAsset(mesh).Bind(a => a.Geometry).Match(
        g => $"{g.VertexCount:N0} vertices, {g.TriangleCount:N0} triangles\nSize {g.Bounds.Size.X:0.###} × {g.Bounds.Size.Y:0.###} × {g.Bounds.Size.Z:0.###}",
        () => "Geometry missing: the source file could not be read.");

    public string Rig => scene.FindAsset(mesh).Bind(a => a.Skeleton).Match(s => $"Skeleton: {s.Count} bones", () => "No skeleton (static mesh)");
}

/// <summary>
/// Bone display settings and the full bone tree of one skeleton. Selecting a bone here selects it
/// in the viewport and the other way round.
/// </summary>
public sealed partial class SkeletonDetailsViewModel : DetailsViewModel, IDisposable
{
    private readonly SkeletonComponent _skeleton;
    private readonly BoneNodeViewModel[] _nodes;
    private bool _syncing;

    public SkeletonDetailsViewModel(SkeletonComponent skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        _skeleton = skeleton;
        var bones = skeleton.Skeleton.Bones;
        _nodes = [.. bones.Select((b, i) => new BoneNodeViewModel(b.Name, i))];
        for (var i = 0; i < bones.Count; i++)
        {
            if (bones[i].Parent >= 0)
            {
                _nodes[bones[i].Parent].Children.Add(_nodes[i]);
            }
            else
            {
                Bones.Add(_nodes[i]);
            }
        }
        UpdateBoneColors();
        SyncFromSkeleton();
        skeleton.SelectedBoneChanged += OnSelectedBoneChanged;
    }

    public override string Title => $"Skeleton of {_skeleton.Parent?.Name}";

    public string Summary => $"{_skeleton.Skeleton.Count} bones, {Bones.Count} root(s)";

    /// <summary>Root bones; each node holds its children, so this is the full hierarchy.</summary>
    public ObservableCollection<BoneNodeViewModel> Bones { get; } = [];

    public bool ShowBones
    {
        get => _skeleton.Visible;
        set => _skeleton.Visible = value;
    }

    public double BoneSize
    {
        get => _skeleton.BoneSize;
        set
        {
            _skeleton.BoneSize = (float)value;
            OnPropertyChanged();
        }
    }

    /// <summary>Choices for <see cref="ColorModeIndex"/>, in <see cref="BoneColorMode"/> order.</summary>
    public IReadOnlyList<string> ColorModes { get; } = ["By side (L blue, R red, center yellow)", "Maya default (all blue)"];

    public int ColorModeIndex
    {
        get => (int)_skeleton.ColorMode;
        set
        {
            if (value < 0)
            {
                return;
            }
            _skeleton.ColorMode = (BoneColorMode)value;
            UpdateBoneColors();
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private BoneNodeViewModel? _selectedBone;

    public string SelectedBoneInfo => _skeleton.SelectedBone.Match(
        index =>
        {
            var bone = _skeleton.Skeleton.Bones[index];
            var parent = bone.Parent >= 0 ? _skeleton.Skeleton.Bones[bone.Parent].Name : "none (root)";
            var children = _skeleton.Skeleton.ChildrenOf(index).Count();
            return $"{bone.Name}\nParent: {parent}\nChildren: {children}\nSide: {BoneDisplayOperation.SideOf(bone.Name)}";
        },
        () => "No bone selected. Click a joint in the viewport or a bone in the tree.");

    [RelayCommand]
    private void ClearSelection() => _skeleton.SelectedBone = Maybe<int>.None;

    [RelayCommand]
    private void ResetBoneSize() => BoneSize = 1;

    public void Dispose() => _skeleton.SelectedBoneChanged -= OnSelectedBoneChanged;

    partial void OnSelectedBoneChanged(BoneNodeViewModel? value)
    {
        if (!_syncing)
        {
            _skeleton.SelectedBone = value is null ? Maybe<int>.None : Maybe<int>.Some(value.Index);
        }
    }

    private void OnSelectedBoneChanged(object? sender, EventArgs e) => SyncFromSkeleton();

    /// <summary>Mirrors the skeleton's selection into the tree, expanding the bone's ancestors so it is visible.</summary>
    private void SyncFromSkeleton()
    {
        _syncing = true;
        try
        {
            foreach (var node in _nodes)
            {
                node.IsHighlighted = false;
            }
            if (_skeleton.SelectedBone.TryGetValue(out var index))
            {
                for (var parent = _skeleton.Skeleton.Bones[index].Parent; parent >= 0; parent = _skeleton.Skeleton.Bones[parent].Parent)
                {
                    _nodes[parent].IsExpanded = true;
                }
                _nodes[index].IsHighlighted = true;
                SelectedBone = _nodes[index];
            }
            else
            {
                SelectedBone = null;
            }
        }
        finally
        {
            _syncing = false;
        }
        OnPropertyChanged(nameof(SelectedBoneInfo));
    }

    private void UpdateBoneColors()
    {
        foreach (var node in _nodes)
        {
            var c = BoneDisplayOperation.ColorOf(node.Name, _skeleton.ColorMode);
            node.Color = new ImmutableSolidColorBrush(Color.FromRgb((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255)));
        }
    }
}

/// <summary>One bone in the details tree.</summary>
public sealed partial class BoneNodeViewModel(string name, int index) : ViewModelBase
{
    public string Name { get; } = name;

    public int Index { get; } = index;

    public ObservableCollection<BoneNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>True for the selected bone, drawn in Maya's selection green.</summary>
    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>The bone's viewport color.</summary>
    [ObservableProperty]
    private IBrush _color = Brushes.Gray;
}
