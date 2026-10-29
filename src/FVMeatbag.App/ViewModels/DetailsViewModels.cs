using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Project;
using FVMeatbag.Core.Rig;
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
    private static readonly CorrectiveRoleCatalog Catalog = CorrectiveRoleCatalog.Default;

    private readonly SkeletonComponent _skeleton;
    private readonly Maybe<SkeletalMeshAsset> _asset;
    private readonly BoneNodeViewModel[] _nodes;
    private bool _syncing;

    public SkeletonDetailsViewModel(SkeletonComponent skeleton, Maybe<SkeletalMeshAsset> asset = default)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        _skeleton = skeleton;
        _asset = asset;
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
        UpdateBadges();
        SyncFromSkeleton();
        skeleton.SelectedBoneChanged += OnSelectedBoneChanged;
        skeleton.Pose.Changed += OnPoseChanged;
        skeleton.Skeleton.Renamed += OnRenamed;
        if (skeleton.RigCheckNode is { } check)
        {
            check.OutputsChanged += OnRigChecked;
        }
        if (asset.TryGetValue(out var a))
        {
            a.Correctives.Changed += OnCorrectivesChanged;
        }
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
    public IReadOnlyList<string> ColorModes { get; } = ["By side", "Default"];

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
            var pose = _skeleton.Pose[index];
            var rest = _skeleton.Pose.Rest[index];
            // Like Maya's joint orient, the rest rotation is factored out so a bone at rest reads 0, 0, 0
            // and turning it about its own Z changes only Z: local = delta, then rest (row vectors).
            var rotate = BoneTransform.EulerDegrees(Quaternion.Concatenate(pose.Rotation, Quaternion.Inverse(rest.Rotation)));
            return $"{bone.Name}\nParent: {parent}\nChildren: {children}\nSide: {BoneNames.SideOf(bone.Name)}\n"
                + $"Translate {Format(pose.Translation)}\nRotate {Format(rotate, 1)}°\nScale {Format(pose.Scale)}"
                + (_skeleton.Pose.IsPosed(index) ? "\nPosed" : "\nAt rest");
        },
        () => "No bone selected. Click a joint in the viewport or a bone in the tree.");

    public bool IsPosed => !_skeleton.Pose.IsRest;

    private Maybe<MannyCheck> Check => Maybe.From(_skeleton.RigCheckNode).Bind(n => n.OutputValues.Check);

    private Maybe<MannyRig> Rig => Check.Bind(c => c.Rig).Where(r => r.SkeletonVersion == _skeleton.Skeleton.Version);

    /// <summary>Whether the skeleton follows the UE5 Manny contract.</summary>
    public string RigSummary => Check.Match(c => c.Summary, () => "Checking the skeleton…");

    public bool HasRigProblems => Check.Match(c => !c.IsConformant, () => false);

    /// <summary>What breaks the Manny contract, a few lines at most.</summary>
    public string RigProblems => Check.Match(
        c => string.Join('\n', c.Problems.Take(8).Select(p => "• " + p.Message))
            + (c.Problems.Count > 8 ? $"\n…and {c.Problems.Count - 8} more" : ""),
        () => "");

    /// <summary>Corrective roles need a Manny skeleton and a mesh asset to store them on.</summary>
    public bool CanEditCorrectives => Rig.HasValue && _asset.HasValue;

    /// <summary>Feedback from the last corrective or rename action.</summary>
    [ObservableProperty]
    private string _correctiveStatus = "";

    /// <summary>Text box contents for renaming the selected bone.</summary>
    [ObservableProperty]
    private string _boneNameEdit = "";

    public bool HasSelectedBone => _skeleton.SelectedBone.HasValue;

    /// <summary>True when the selected bone is an extra (not part of the Manny body) of a Manny skeleton.</summary>
    public bool IsSelectedExtra => CanEditCorrectives && SelectedExtra.HasValue;

    public string SelectedBoneKind => Rig.Match(
        rig => _skeleton.SelectedBone.Match(
            bone => rig.IsExtra(bone)
                ? $"Extra bone, follows {_skeleton.Skeleton.Bones[rig.DriverOf(bone)].Name}"
                : "Core UE5 Manny bone",
            () => ""),
        () => "");

    /// <summary>Choices for <see cref="RoleIndex"/>: "(none)", then the catalog's roles.</summary>
    public IReadOnlyList<string> RoleChoices { get; } = [.. Catalog.Roles.Select(r => r.Name).Prepend("(none)")];

    public int RoleIndex
    {
        get => Binding.Tag.Bind(t => Catalog.Roles.Select((r, i) => (r, i)).FirstOrNone(x => x.r.Id == t.Role)).Match(x => x.i + 1, () => 0);
        set
        {
            if (value < 0 || !SelectedExtraName.TryGetValue(out var name))
            {
                return;
            }
            if (value == 0)
            {
                Correctives.Tag(name, Maybe<CorrectiveTag>.None);
                return;
            }
            var role = Catalog.Roles[value - 1];
            var side = role.Sided ? Binding.Tag.Match(t => t.Side, () => BoneNames.SideOf(name)) : BoneSide.Center;
            Correctives.Tag(name, new CorrectiveTag(role.Id, side, Binding.Tag.Bind(t => t.Part)));
        }
    }

    /// <summary>Choices for <see cref="SideIndex"/>, in <see cref="BoneSide"/> order.</summary>
    public IReadOnlyList<string> SideChoices { get; } = ["Center", "Left", "Right"];

    public int SideIndex
    {
        get => Binding.Tag.Match(t => (int)t.Side, () => -1);
        set => EditTag(t => t with { Side = (BoneSide)value }, value >= 0);
    }

    /// <summary>Optional part of the role (hi, lo, inner); empty for none.</summary>
    public string Part
    {
        get => Binding.Tag.Bind(t => t.Part).OrDefault("");
        set => EditTag(t => t with { Part = string.IsNullOrWhiteSpace(value) ? Maybe<string>.None : value.Trim() }, true);
    }

    public bool HasRole => Binding.Tag.HasValue;

    /// <summary>Choices for <see cref="ModeIndex"/>, in <see cref="CorrectiveMode"/> order.</summary>
    public IReadOnlyList<string> ModeChoices { get; } = ["Solve", "Lock", "Ignore"];

    public int ModeIndex
    {
        get => (int)Binding.Mode;
        set
        {
            if (value >= 0 && SelectedExtraName.TryGetValue(out var name))
            {
                Correctives.SetMode(name, (CorrectiveMode)value);
            }
        }
    }

    private Maybe<int> SelectedExtra => Rig.Bind(rig => _skeleton.SelectedBone.Where(rig.IsExtra));

    private Maybe<string> SelectedExtraName => SelectedExtra.Map(b => _skeleton.Skeleton.Bones[b].Name);

    private CorrectiveSetup Correctives => _asset.Value.Correctives;

    private CorrectiveBinding Binding => SelectedExtraName.Match(n => Correctives[n], () => CorrectiveBinding.Default);

    /// <summary>Tags every untagged extra whose name says what it is (bicep_l, quad_upper_r, ...).</summary>
    [RelayCommand]
    private void SuggestRoles()
    {
        if (!CanEditCorrectives)
        {
            return;
        }
        var tagged = Correctives.SuggestAll(Rig.Value, Catalog);
        var untagged = Rig.Value.Extras.Count(b => Correctives[_skeleton.Skeleton.Bones[b].Name].Tag.IsNone);
        CorrectiveStatus = $"Tagged {tagged} bone{(tagged == 1 ? "" : "s")} from their names; {untagged} extra{(untagged == 1 ? "" : "s")} without a role.";
    }

    /// <summary>Copies the selected bone's role and mode to its other-side twin.</summary>
    [RelayCommand]
    private void MirrorCorrective()
    {
        if (!SelectedExtra.TryGetValue(out var bone))
        {
            return;
        }
        CorrectiveStatus = Correctives.Mirror(Rig.Value, bone).Match(
            other => $"Copied to {_skeleton.Skeleton.Bones[other].Name}.",
            () => $"No extra bone named {BoneNames.Mirror(_skeleton.Skeleton.Bones[bone].Name)} to mirror to.");
    }

    [RelayCommand]
    private void RenameBone()
    {
        if (!_skeleton.SelectedBone.TryGetValue(out var bone) || !_asset.TryGetValue(out var asset) || string.IsNullOrWhiteSpace(BoneNameEdit))
        {
            return;
        }
        var old = _skeleton.Skeleton.Bones[bone].Name;
        CorrectiveStatus = asset.RenameBone(bone, BoneNameEdit)
            ? $"Renamed {old} to {_skeleton.Skeleton.Bones[bone].Name}."
            : $"Another bone is already named {BoneNameEdit.Trim()}.";
    }

    [RelayCommand]
    private void ClearSelection() => _skeleton.SelectedBone = Maybe<int>.None;

    /// <summary>Puts the selected bone back into its rest transform.</summary>
    [RelayCommand]
    private void ResetBone()
    {
        if (_skeleton.SelectedBone.TryGetValue(out var bone))
        {
            _skeleton.Pose.ResetBone(bone);
        }
    }

    /// <summary>Puts every bone back into its rest transform.</summary>
    [RelayCommand]
    private void ResetPose() => _skeleton.Pose.Reset();

    [RelayCommand]
    private void ResetBoneSize() => BoneSize = 1;

    public void Dispose()
    {
        _skeleton.SelectedBoneChanged -= OnSelectedBoneChanged;
        _skeleton.Pose.Changed -= OnPoseChanged;
        _skeleton.Skeleton.Renamed -= OnRenamed;
        if (_skeleton.RigCheckNode is { } check)
        {
            check.OutputsChanged -= OnRigChecked;
        }
        if (_asset.TryGetValue(out var asset))
        {
            asset.Correctives.Changed -= OnCorrectivesChanged;
        }
    }

    partial void OnSelectedBoneChanged(BoneNodeViewModel? value)
    {
        if (!_syncing)
        {
            _skeleton.SelectedBone = value is null ? Maybe<int>.None : Maybe<int>.Some(value.Index);
        }
    }

    private void OnSelectedBoneChanged(object? sender, EventArgs e) => SyncFromSkeleton();

    private void OnRenamed(object? sender, EventArgs e)
    {
        foreach (var node in _nodes)
        {
            node.Name = _skeleton.Skeleton.Bones[node.Index].Name;
        }
        UpdateBoneColors();
        BoneNameEdit = _skeleton.SelectedBone.Match(b => _skeleton.Skeleton.Bones[b].Name, () => "");
        OnPropertyChanged(nameof(SelectedBoneInfo));
    }

    private void OnRigChecked(object? sender, OutputsChangedEventArgs e)
    {
        UpdateBadges();
        OnCorrectiveStateChanged();
    }

    private void OnCorrectivesChanged(object? sender, EventArgs e)
    {
        UpdateBadges();
        OnCorrectiveStateChanged();
    }

    private void OnCorrectiveStateChanged()
    {
        foreach (var name in (ReadOnlySpan<string>)[nameof(RigSummary), nameof(HasRigProblems), nameof(RigProblems), nameof(CanEditCorrectives),
            nameof(IsSelectedExtra), nameof(SelectedBoneKind), nameof(RoleIndex), nameof(SideIndex), nameof(Part), nameof(HasRole), nameof(ModeIndex)])
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>Shows each extra's role (or mode) next to its name in the tree.</summary>
    private void UpdateBadges()
    {
        var rig = Rig;
        foreach (var node in _nodes)
        {
            node.Badge = rig.Where(r => r.IsExtra(node.Index) && _asset.HasValue).Match(
                _ =>
                {
                    var binding = Correctives[node.Name];
                    return binding.Mode == CorrectiveMode.Ignore ? "ignored"
                        : binding.Tag.Match(t => t.CanonicalName + (binding.Mode == CorrectiveMode.Lock ? " (locked)" : ""), () => "no role");
                },
                () => "");
        }
    }

    private void EditTag(Func<CorrectiveTag, CorrectiveTag> edit, bool valid)
    {
        if (valid && SelectedExtraName.TryGetValue(out var name) && Correctives[name].Tag.TryGetValue(out var tag))
        {
            Correctives.Tag(name, edit(tag));
        }
    }

    private void OnPoseChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SelectedBoneInfo));
        OnPropertyChanged(nameof(IsPosed));
    }

    /// <summary>Formats with <paramref name="decimals"/> digits, without printing "-0" for tiny negatives.</summary>
    private static string Format(Vector3 v, int decimals = 3) =>
        string.Join(", ", new[] { v.X, v.Y, v.Z }.Select(f =>
        {
            var rounded = MathF.Round(f, decimals);
            return (rounded == 0f ? 0f : rounded).ToString(CultureInfo.CurrentCulture);
        }));

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
        BoneNameEdit = _skeleton.SelectedBone.Match(b => _skeleton.Skeleton.Bones[b].Name, () => "");
        CorrectiveStatus = "";
        OnPropertyChanged(nameof(SelectedBoneInfo));
        OnPropertyChanged(nameof(HasSelectedBone));
        OnCorrectiveStateChanged();
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
    [ObservableProperty]
    private string _name = name;

    /// <summary>The corrective role (or mode) of an extra bone; empty for core bones.</summary>
    [ObservableProperty]
    private string _badge = "";

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
