using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>Template sizes are in body units (pelvis to head); 1 without a Manny body.</summary>
    private float Unit => Body.Match(b => b.Unit, () => 1f);

    public string Summary => Muscles.Count == 0
        ? "No muscles yet. Add the body template to start."
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

    /// <summary>The template places muscles by Manny bone names, so it needs pelvis, head and both upper arms.</summary>
    public bool CanUseTemplate => Body.HasValue;

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
        m => $"{m.Name}\n{m.Origin.Bone} → {m.Insertion.Bone}\n{m.Shape}, {m.Side}"
            + m.Template.Match(t => $", from template {t}", () => ""),
        () => "No muscle selected. Pick one in the list.");

    /// <summary>Belly half-width in body units (pelvis to head).</summary>
    public double Radius
    {
        get => Selected.Match(m => m.Radius / Unit, () => 0f);
        set => Edit(m => m with { Radius = (float)value * Unit });
    }

    /// <summary>Depth over width of the belly's section: 1 round, lower flatter.</summary>
    public double Flatness
    {
        get => Selected.Match(m => m.Flatness, () => 1f);
        set => Edit(m => m with { Flatness = (float)value });
    }

    /// <summary>Where the bulk sits: positive a fuller belly, negative a slimmer one.</summary>
    public double Thickness
    {
        get => Selected.Match(m => m.Thickness, () => 0f);
        set => Edit(m => m with { Thickness = (float)value });
    }

    /// <summary>How strongly the belly bulges when the muscle shortens: 1 keeps its volume, 0 never bulges.</summary>
    public double Volume
    {
        get => Selected.Match(m => m.Volume, () => 1f);
        set => Edit(m => m with { Volume = (float)value });
    }

    public double OriginTendon
    {
        get => Selected.Match(m => m.Tendons.X, () => 0f);
        set => Edit(m => m with { Tendons = m.Tendons with { X = (float)value } });
    }

    public double InsertionTendon
    {
        get => Selected.Match(m => m.Tendons.Y, () => 0f);
        set => Edit(m => m with { Tendons = m.Tendons with { Y = (float)value } });
    }

    /// <summary>Choices for <see cref="ActionIndex"/>, in <see cref="MuscleAction"/> order.</summary>
    public IReadOnlyList<string> ActionChoices { get; } = ["Flexor: bulges when it shortens", "Extensor: bulges when it lengthens"];

    public int ActionIndex
    {
        get => Selected.Match(m => (int)m.Action, () => -1);
        set
        {
            if (value >= 0)
            {
                Edit(m => m with { Action = (MuscleAction)value });
            }
        }
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

    /// <summary>Adds every template muscle the mesh does not have yet; existing ones are kept as they are.</summary>
    [RelayCommand]
    private void AddTemplate()
    {
        if (!Body.TryGetValue(out var body))
        {
            Status = "The template needs a UE5 Manny skeleton (pelvis, head, upperarm_l, upperarm_r).";
            return;
        }
        var report = MuscleTemplate.Default.Apply(body, Muscles);
        var already = report.Skipped.Count(s => s.EndsWith(": already there", StringComparison.Ordinal));
        Status = report.Added.Count == 0 && already == report.Skipped.Count
            ? "Every template muscle is already there."
            : report.Summary + (report.Skipped.Count > already ? "\n" + string.Join('\n', report.Skipped.Where(s => !s.EndsWith(": already there", StringComparison.Ordinal))) : "")
                + " Save (Ctrl+S) to keep them.";
    }

    /// <summary>Puts the selected muscle back to its template placement and shape; its drives are reset too.</summary>
    [RelayCommand]
    private void ResetToTemplate()
    {
        if (!Selected.TryGetValue(out var muscle) || !Body.TryGetValue(out var body))
        {
            return;
        }
        Status = muscle.Template.Bind(MuscleTemplate.Default.Find).Bind(entry => MuscleTemplate.Build(entry, muscle.Side, body)).Match(
            rebuilt =>
            {
                Muscles.Set(rebuilt);
                return $"Reset {muscle.Name} to the template.";
            },
            () => $"{muscle.Name} has no template to reset to.");
    }

    /// <summary>Copies the selected muscle to the other side, replacing the twin if there is one.</summary>
    [RelayCommand]
    private void Mirror()
    {
        if (!Selected.TryGetValue(out var muscle) || !Body.TryGetValue(out var body))
        {
            return;
        }
        Status = muscle.Side == BoneSide.Center
            ? $"{muscle.Name} sits on the center line; there is nothing to mirror to."
            : Muscles.Mirror(muscle.Name, body).Match(
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
