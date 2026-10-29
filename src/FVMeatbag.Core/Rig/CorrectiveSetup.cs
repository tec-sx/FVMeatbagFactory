using FVMeatbag.Graph;

namespace FVMeatbag.Core.Rig;

/// <summary>What the corrective solve does with an extra bone.</summary>
public enum CorrectiveMode
{
    /// <summary>The solver fits the bone's weights and pose transforms.</summary>
    Solve,

    /// <summary>The bone keeps its imported weights and transforms; the solver works around it.</summary>
    Lock,

    /// <summary>Not a corrective (a socket, a face bone handled elsewhere); left alone and not exported as one.</summary>
    Ignore,
}

/// <param name="Tag">The bone's role; None for a corrective without one (solved without a muscle to follow).</param>
public readonly record struct CorrectiveBinding(Maybe<CorrectiveTag> Tag, CorrectiveMode Mode)
{
    public static CorrectiveBinding Default => new(Maybe<CorrectiveTag>.None, CorrectiveMode.Solve);
}

/// <summary>
/// The corrective setup of one skeletal mesh: for each extra bone, its role and mode. Keyed by bone
/// name, saved with the project. Bones without an entry are untagged and solved.
/// </summary>
public sealed class CorrectiveSetup
{
    private readonly Dictionary<string, CorrectiveBinding> _bindings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bumped on every change, for graph change detection.</summary>
    public long Version { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Bones with a role or a non-default mode.</summary>
    public IReadOnlyDictionary<string, CorrectiveBinding> Bindings => _bindings;

    public CorrectiveBinding this[string bone] => _bindings.GetValueOrDefault(bone, CorrectiveBinding.Default);

    public void Set(string bone, CorrectiveBinding binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bone);
        if (this[bone] == binding)
        {
            return;
        }
        if (binding == CorrectiveBinding.Default)
        {
            _bindings.Remove(bone);
        }
        else
        {
            _bindings[bone] = binding;
        }
        OnChanged();
    }

    /// <summary>Marks a bone as a role, e.g. "biceps, left, hi"; None clears the role.</summary>
    public void Tag(string bone, Maybe<CorrectiveTag> tag) => Set(bone, this[bone] with { Tag = tag });

    public void SetMode(string bone, CorrectiveMode mode) => Set(bone, this[bone] with { Mode = mode });

    /// <summary>The bone tagged with <paramref name="tag"/>, if any.</summary>
    public Maybe<int> FindBone(MannyRig rig, CorrectiveTag tag)
    {
        ArgumentNullException.ThrowIfNull(rig);
        foreach (var bone in rig.Extras)
        {
            if (this[rig.Skeleton.Bones[bone].Name].Tag == tag)
            {
                return bone;
            }
        }
        return Maybe<int>.None;
    }

    /// <summary>
    /// Tags every untagged extra whose name the catalog recognises, skipping bones set to
    /// <see cref="CorrectiveMode.Ignore"/> and tags another bone already has. Returns how many were tagged.
    /// </summary>
    public int SuggestAll(MannyRig rig, CorrectiveRoleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(catalog);
        var tagged = 0;
        foreach (var bone in rig.Extras)
        {
            var name = rig.Skeleton.Bones[bone].Name;
            var current = this[name];
            if (current.Tag.HasValue || current.Mode == CorrectiveMode.Ignore)
            {
                continue;
            }
            if (catalog.Suggest(name).Where(t => FindBone(rig, t).IsNone).TryGetValue(out var tag))
            {
                Tag(name, tag);
                tagged++;
            }
        }
        return tagged;
    }

    /// <summary>
    /// Copies the bone's binding to the bone with the mirrored name (<c>bicep_l</c> → <c>bicep_r</c>),
    /// with the side flipped. Returns that bone, or None if the skeleton has no such extra.
    /// </summary>
    public Maybe<int> Mirror(MannyRig rig, int bone)
    {
        ArgumentNullException.ThrowIfNull(rig);
        var name = rig.Skeleton.Bones[bone].Name;
        var mirroredName = BoneNames.Mirror(name);
        if (string.Equals(mirroredName, name, StringComparison.OrdinalIgnoreCase))
        {
            return Maybe<int>.None;
        }
        return rig.Skeleton.FindBone(mirroredName).Where(rig.IsExtra).Map(other =>
        {
            var binding = this[name];
            Set(mirroredName, binding with { Tag = binding.Tag.Map(t => t.Mirrored) });
            return other;
        });
    }

    /// <summary>Moves a binding along with a renamed bone.</summary>
    public void RenameBone(string oldName, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        if (!_bindings.Remove(oldName, out var binding))
        {
            return;
        }
        _bindings[newName] = binding;
        OnChanged();
    }

    private void OnChanged()
    {
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
