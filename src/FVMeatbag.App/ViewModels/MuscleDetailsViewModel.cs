using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Muscles;
using FVMeatbag.Core.Project;
using FVMeatbag.Core.Rig;
using FVMeatbag.Graph;
using FVMeatbag.Scene;
using FVMeatbag.Scene.Components;

namespace FVMeatbag.App.ViewModels;

/// <summary>
/// The muscle preview of one skeletal mesh: display settings, the muscle list, and the selected
/// muscle's shape and the corrective roles it drives. Muscles are a preview; only the correctives
/// they drive go to Unreal.
/// </summary>
public sealed partial class MuscleDetailsViewModel : DetailsViewModel, IDisposable
{
    private static readonly CorrectiveRoleCatalog Catalog = CorrectiveRoleCatalog.Default;

    private readonly SceneRoot _scene;
    private readonly MuscleComponent _component;
    private readonly Maybe<SkeletalMeshAsset> _asset;
    private bool _syncing;

    public MuscleDetailsViewModel(SceneRoot scene, MuscleComponent component, Maybe<SkeletalMeshAsset> asset = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(component);
        _scene = scene;
        _component = component;
        _asset = asset;
        RebuildItems();
        SyncSelection();
        component.Muscles.Changed += OnMusclesChanged;
        component.SelectedMuscleChanged += OnSelectedMuscleChanged;
        if (component.RigNode is { } rig)
        {
            rig.OutputsChanged += OnRigChanged;
        }
        if (asset.TryGetValue(out var a))
        {
            a.Correctives.Changed += OnCorrectivesChanged;
        }
    }

    public override string Title => $"Muscles of {_component.Skeleton?.Parent?.Name}";

    private MuscleSet Muscles => _component.Muscles;

    private Maybe<BodyFrame> Body => Maybe.From(_component.Skeleton).Bind(s => BodyFrame.From(s.Skeleton));

    /// <summary>The rest mesh, for rays to the skin; built once per mesh.</summary>
    private Maybe<SkinSurface> Skin => _asset.Bind(a => a.Geometry).Map(g => Skins.GetValue(g, SkinSurface.From));

    private static readonly ConditionalWeakTable<MeshGeometry, SkinSurface> Skins = [];

    /// <summary>Sizes are shown in body units (pelvis to head); 1 without a Manny body.</summary>
    private float Unit => Body.Match(b => b.Unit, () => 1f);

    public string Summary => Muscles.Count == 0
        ? "No muscles yet. Build them from the body template to start."
        : $"{Muscles.Count} muscle{(Muscles.Count == 1 ? "" : "s")}";

    /// <summary>Muscles whose bones are missing, a few lines at most.</summary>
    public string Problems
    {
        get
        {
            var problems = Maybe.From(_component.RigNode).Match(n => n.OutputValues.Problems, () => []);
            return string.Join('\n', problems.Take(6).Select(p => "• " + p))
                + (problems.Count > 6 ? $"\n…and {problems.Count - 6} more" : "");
        }
    }

    public bool HasProblems => Problems.Length > 0;

    /// <summary>The template places muscles by Manny bone names under the skin, so it needs pelvis, head, both upper arms and the mesh.</summary>
    public bool CanUseTemplate => Body.HasValue && Skin.HasValue;

    public bool ShowMuscles
    {
        get => _component.Visible;
        set => _component.Visible = value;
    }

    public double Opacity
    {
        get => _scene.MuscleOpacity;
        set
        {
            _scene.MuscleOpacity = (float)value;
            OnPropertyChanged();
        }
    }

    /// <summary>X-ray: draw the muscles over the skin instead of hiding them behind it.</summary>
    public bool ThroughSkin
    {
        get => _scene.MusclesThroughSkin;
        set => _scene.MusclesThroughSkin = value;
    }

    public ObservableCollection<MuscleItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private MuscleItemViewModel? _selectedItem;

    /// <summary>Feedback from the last template or mirror action.</summary>
    [ObservableProperty]
    private string _status = "";

    private Maybe<Muscle> Selected => _component.SelectedMuscle.Map(i => Muscles.Muscles[i]);

    public bool HasSelection => Selected.HasValue;

    public string SelectedInfo => Selected.Match(
        m => $"{m.Name}{(m.Group != m.Name ? $", part of {m.Group}" : "")}\n{m.Origin.Bone} → {m.Insertion.Bone}"
            + (m.Hooks.Count > 0 ? $", bends with {string.Join(", ", m.Hooks.Select(h => h.Bone))}" : "")
            + (m.Fit.HasValue ? "\nFitted to the skin" : "\nNot fitted to the skin"),
        () => "No muscle selected. Pick one in the list.");

    /// <summary>Belly radius across the muscle, in body units (pelvis to head).</summary>
    public double Width
    {
        get => Selected.Match(m => m.Width / Unit, () => 0f);
        set => Edit(m => m with { Width = (float)value * Unit });
    }

    /// <summary>Belly radius towards the skin, in body units (pelvis to head).</summary>
    public double Thickness
    {
        get => Selected.Match(m => m.Thickness / Unit, () => 0f);
        set => Edit(m => m with { Thickness = (float)value * Unit });
    }

    /// <summary>Where the strand is widest, 0 at the origin to 1 at the insertion.</summary>
    public double Belly
    {
        get => Selected.Match(m => m.Belly, () => Muscle.DefaultBelly);
        set => Edit(m => m with { Belly = (float)value });
    }

    /// <summary>Radius at the ends, relative to the belly.</summary>
    public double Tendon
    {
        get => Selected.Match(m => m.Tendon, () => Muscle.DefaultTendon);
        set => Edit(m => m with { Tendon = (float)value });
    }

    /// <summary>How strongly the belly bulges when the muscle shortens: 1 keeps its volume, 0 never bulges.</summary>
    public double Volume
    {
        get => Selected.Match(m => m.Volume, () => 1f);
        set => Edit(m => m with { Volume = (float)value });
    }

    /// <summary>Corrective roles the selected muscle drives; many muscles may drive one role.</summary>
    public ObservableCollection<MuscleDriveViewModel> Drives { get; } = [];

    /// <summary>Choices for a drive's role, in catalog order.</summary>
    public IReadOnlyList<string> RoleChoices { get; } = [.. Catalog.Roles.Select(r => r.Name)];

    /// <summary>The tagged corrective bones the selected muscle moves, with the weight it moves them by.</summary>
    public string DrivenCorrectives => Selected.Match(
        muscle =>
        {
            if (!_asset.TryGetValue(out var asset))
            {
                return "";
            }
            var driven = asset.Correctives.Bindings
                .Where(b => b.Value.Mode != CorrectiveMode.Ignore && b.Value.Tag.HasValue)
                .Select(b => (Bone: b.Key, Weight: muscle.DriveWeight(b.Value.Tag.Value)))
                .Where(x => x.Weight > 0f)
                .OrderByDescending(x => x.Weight)
                .ThenBy(x => x.Bone, StringComparer.Ordinal)
                .Select(x => $"{x.Bone} ({x.Weight:0.##})")
                .ToList();
            return driven.Count == 0
                ? "Moves no corrective bone yet: tag extra bones with these roles in the skeleton's details."
                : "Moves " + string.Join(", ", driven);
        },
        () => "");

    /// <summary>
    /// Builds the body template like the Flicker Void tools: every muscle placed under the skin on the
    /// left, mirrored to the right, fitted to the skin. Replaces earlier template muscles and keeps
    /// hand-made ones.
    /// </summary>
    [RelayCommand]
    private void AddTemplate()
    {
        if (!Body.TryGetValue(out var body) || !Skin.TryGetValue(out var skin))
        {
            Status = "The template needs a UE5 Manny skeleton (pelvis, head, upperarm_l, upperarm_r) and its mesh.";
            return;
        }
        var report = MuscleTemplate.Default.Generate(body, skin, Muscles);
        Status = report.Missing.Count > 0 ? report.Summary : report.Summary + " Save (Ctrl+S) to keep them.";
    }

    /// <summary>Puts the selected muscle back to its template placement, shape and drives, fitted to the skin.</summary>
    [RelayCommand]
    private void ResetToTemplate()
    {
        if (!Selected.TryGetValue(out var muscle) || !Body.TryGetValue(out var body) || !Skin.TryGetValue(out var skin))
        {
            return;
        }
        Status = MuscleTemplate.Default.Rebuild(muscle, body, skin).Match(
            rebuilt =>
            {
                Muscles.Set(rebuilt);
                return $"Reset {muscle.Name} to the template.";
            },
            () => $"{muscle.Name} has no template to reset to.");
    }

    /// <summary>Gives the selected muscle's rings the room under the skin; see <see cref="MuscleShape.FitToSkin"/>.</summary>
    [RelayCommand]
    private void FitToSkin()
    {
        if (Selected.TryGetValue(out var muscle) && Body.TryGetValue(out var body) && Skin.TryGetValue(out var skin))
        {
            Muscles.Set(body.FitToSkin(muscle with { Fit = Maybe<IReadOnlyList<float>>.None }, skin));
            Status = $"Fitted {muscle.Name} to the skin.";
        }
    }

    [RelayCommand]
    private void FitAllToSkin()
    {
        if (Body.TryGetValue(out var body) && Skin.TryGetValue(out var skin))
        {
            foreach (var muscle in Muscles.Muscles.ToList())
            {
                Muscles.Set(body.FitToSkin(muscle with { Fit = Maybe<IReadOnlyList<float>>.None }, skin));
            }
            Status = $"Fitted {Muscles.Count} muscles to the skin.";
        }
    }

    /// <summary>Copies the selected muscle to the other side, replacing the twin if there is one, and fits it there.</summary>
    [RelayCommand]
    private void Mirror()
    {
        if (!Selected.TryGetValue(out var muscle) || !Body.TryGetValue(out var body))
        {
            return;
        }
        Status = muscle.Side == BoneSide.Center
            ? $"{muscle.Name} sits on the center line; there is nothing to mirror to."
            : Muscles.Mirror(muscle.Name, body, Skin).Match(
                twin => $"Copied to {twin.Name}.",
                () => $"The skeleton lacks the other side's bones for {muscle.Name}.");
    }

    [RelayCommand]
    private void Remove()
    {
        if (Selected.TryGetValue(out var muscle))
        {
            _component.SelectedMuscle = Maybe<int>.None;
            Muscles.Remove(muscle.Name);
            Status = $"Removed {muscle.Name}. Add the template again to bring it back.";
        }
    }

    [RelayCommand]
    private void AddDrive()
    {
        if (Catalog.Roles.Count > 0)
        {
            Edit(m => m with { Drives = [.. m.Drives, new MuscleDrive(Catalog.Roles[0].Id, Maybe<string>.None, 1f)] });
        }
    }

    [RelayCommand]
    private void ClearSelection() => _component.SelectedMuscle = Maybe<int>.None;

    /// <summary>Replaces the selected muscle's drive at <paramref name="index"/>; None removes it.</summary>
    internal void EditDrive(int index, Maybe<MuscleDrive> drive) => Edit(m =>
    {
        if (index < 0 || index >= m.Drives.Count)
        {
            return m;
        }
        var drives = m.Drives.ToList();
        if (drive.TryGetValue(out var d))
        {
            drives[index] = d;
        }
        else
        {
            drives.RemoveAt(index);
        }
        return m with { Drives = drives };
    });

    public void Dispose()
    {
        _component.Muscles.Changed -= OnMusclesChanged;
        _component.SelectedMuscleChanged -= OnSelectedMuscleChanged;
        if (_component.RigNode is { } rig)
        {
            rig.OutputsChanged -= OnRigChanged;
        }
        if (_asset.TryGetValue(out var asset))
        {
            asset.Correctives.Changed -= OnCorrectivesChanged;
        }
    }

    partial void OnSelectedItemChanged(MuscleItemViewModel? value)
    {
        if (!_syncing)
        {
            _component.SelectedMuscle = value is null ? Maybe<int>.None : Maybe<int>.Some(value.Index);
        }
    }

    private void Edit(Func<Muscle, Muscle> edit)
    {
        if (!_syncing && Selected.TryGetValue(out var muscle))
        {
            Muscles.Set(edit(muscle));
        }
    }

    private void OnMusclesChanged(object? sender, EventArgs e)
    {
        // Rebuilding the lists while a slider is dragged would drop the drag, so rows are only
        // rebuilt when muscles come or go, and updated in place otherwise.
        if (Items.Count != Muscles.Count || Items.Where((item, i) => item.Name != Muscles.Muscles[i].Name).Any())
        {
            RebuildItems();
        }
        else
        {
            foreach (var item in Items)
            {
                item.Badge = BadgeOf(Muscles.Muscles[item.Index]);
            }
        }
        SyncSelection();
    }

    private void OnSelectedMuscleChanged(object? sender, EventArgs e) => SyncSelection();

    private void OnRigChanged(object? sender, OutputsChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Problems));
        OnPropertyChanged(nameof(HasProblems));
    }

    private void OnCorrectivesChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(DrivenCorrectives));

    private void RebuildItems()
    {
        _syncing = true;
        try
        {
            Items.Clear();
            for (var i = 0; i < Muscles.Count; i++)
            {
                Items.Add(new MuscleItemViewModel(Muscles.Muscles[i].Name, i) { Badge = BadgeOf(Muscles.Muscles[i]) });
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SyncSelection()
    {
        _syncing = true;
        try
        {
            SelectedItem = _component.SelectedMuscle.Match(i => i < Items.Count ? Items[i] : null, () => null);
            SyncDrives();
        }
        finally
        {
            _syncing = false;
        }
        OnPropertyChanged(string.Empty);
    }

    private void SyncDrives()
    {
        var drives = Selected.Match(m => m.Drives, () => []);
        if (Drives.Count != drives.Count)
        {
            Drives.Clear();
            for (var i = 0; i < drives.Count; i++)
            {
                Drives.Add(new MuscleDriveViewModel(this, i));
            }
        }
        for (var i = 0; i < drives.Count; i++)
        {
            Drives[i].Load(drives[i]);
        }
    }

    private static string BadgeOf(Muscle muscle) => string.Join(", ", muscle.Drives.OrderByDescending(d => d.Weight).Take(2).Select(d => d.Role));
}

/// <summary>One muscle in the details list.</summary>
public sealed partial class MuscleItemViewModel(string name, int index) : ViewModelBase
{
    public string Name { get; } = name;

    public int Index { get; } = index;

    /// <summary>The main corrective roles it drives.</summary>
    [ObservableProperty]
    private string _badge = "";
}

/// <summary>One corrective role a muscle drives, with its weight.</summary>
public sealed partial class MuscleDriveViewModel(MuscleDetailsViewModel owner, int index) : ViewModelBase
{
    private static readonly CorrectiveRoleCatalog Catalog = CorrectiveRoleCatalog.Default;

    private MuscleDrive _drive;
    private bool _loading;

    public IReadOnlyList<string> RoleChoices => owner.RoleChoices;

    public int RoleIndex
    {
        get => Catalog.Roles.Select((r, i) => (r, i)).FirstOrNone(x => x.r.Id == _drive.Role).Match(x => x.i, () => -1);
        set
        {
            if (value >= 0 && value < Catalog.Roles.Count)
            {
                Save(_drive with { Role = Catalog.Roles[value].Id });
            }
        }
    }

    /// <summary>Optional part of the role (hi, lo, inner); empty drives every part.</summary>
    public string Part
    {
        get => _drive.Part.OrDefault("");
        set => Save(_drive with { Part = string.IsNullOrWhiteSpace(value) ? Maybe<string>.None : value.Trim() });
    }

    public double Weight
    {
        get => _drive.Weight;
        set => Save(_drive with { Weight = (float)value });
    }

    [RelayCommand]
    private void Remove() => owner.EditDrive(index, Maybe<MuscleDrive>.None);

    internal void Load(MuscleDrive drive)
    {
        _loading = true;
        try
        {
            _drive = drive;
            OnPropertyChanged(string.Empty);
        }
        finally
        {
            _loading = false;
        }
    }

    private void Save(MuscleDrive drive)
    {
        if (!_loading && drive != _drive)
        {
            _drive = drive;
            owner.EditDrive(index, drive);
        }
    }
}
