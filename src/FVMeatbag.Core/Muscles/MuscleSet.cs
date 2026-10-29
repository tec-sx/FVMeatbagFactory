using FVMeatbag.Core.Rig;
using FVMeatbag.Graph;

namespace FVMeatbag.Core.Muscles;

/// <summary>
/// The muscles of one skeletal mesh, in a stable order, saved with the project. Names are unique
/// ignoring case.
/// </summary>
public sealed class MuscleSet
{
    private readonly List<Muscle> _muscles = [];

    /// <summary>Bumped on every change, for graph change detection.</summary>
    public long Version { get; private set; }

    public event EventHandler? Changed;

    public IReadOnlyList<Muscle> Muscles => _muscles;

    public int Count => _muscles.Count;

    public Maybe<int> IndexOf(string name) =>
        Enumerable.Range(0, _muscles.Count).FirstOrNone(i => string.Equals(_muscles[i].Name, name, StringComparison.OrdinalIgnoreCase));

    public Maybe<Muscle> Find(string name) => IndexOf(name).Map(i => _muscles[i]);

    /// <summary>Adds the muscle, or replaces the one with the same name in place.</summary>
    public void Set(Muscle muscle)
    {
        ArgumentNullException.ThrowIfNull(muscle);
        ArgumentException.ThrowIfNullOrWhiteSpace(muscle.Name);
        if (IndexOf(muscle.Name).TryGetValue(out var index))
        {
            if (_muscles[index] == muscle)
            {
                return;
            }
            _muscles[index] = muscle;
        }
        else
        {
            _muscles.Add(muscle);
        }
        OnChanged();
    }

    public bool Remove(string name)
    {
        if (!IndexOf(name).TryGetValue(out var index))
        {
            return false;
        }
        _muscles.RemoveAt(index);
        OnChanged();
        return true;
    }

    public void Clear()
    {
        if (_muscles.Count == 0)
        {
            return;
        }
        _muscles.Clear();
        OnChanged();
    }

    /// <summary>
    /// Copies a sided muscle to the other side, replacing its twin: mirrored name and bones
    /// (<c>upperarm_l</c> → <c>upperarm_r</c>), attachments, hooks and facing reflected across the
    /// skeleton's mirror plane, then fitted to <paramref name="skin"/> when given. Returns the new
    /// muscle, or None for a center muscle or when the skeleton lacks the mirrored bones.
    /// </summary>
    public Maybe<Muscle> Mirror(string name, BodyFrame body, Maybe<SkinSurface> skin = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!Find(name).TryGetValue(out var muscle) || muscle.Side == BoneSide.Center)
        {
            return Maybe<Muscle>.None;
        }
        return body.Mirror(muscle).Map(mirrored =>
        {
            var fitted = skin.Match(s => body.FitToSkin(mirrored, s), () => mirrored);
            Set(fitted);
            return fitted;
        });
    }

    /// <summary>Points muscles attached to a renamed bone at its new name.</summary>
    public void RenameBone(string oldName, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var changed = false;
        for (var i = 0; i < _muscles.Count; i++)
        {
            var muscle = _muscles[i];
            if (!new[] { muscle.Origin, muscle.Insertion }.Concat(muscle.Hooks).Any(a => string.Equals(a.Bone, oldName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            _muscles[i] = muscle with
            {
                Origin = Rename(muscle.Origin, oldName, newName),
                Insertion = Rename(muscle.Insertion, oldName, newName),
                Hooks = [.. muscle.Hooks.Select(h => Rename(h, oldName, newName))],
            };
            changed = true;
        }
        if (changed)
        {
            OnChanged();
        }
    }

    private static MuscleAttachment Rename(MuscleAttachment attachment, string oldName, string newName) =>
        string.Equals(attachment.Bone, oldName, StringComparison.OrdinalIgnoreCase) ? attachment with { Bone = newName } : attachment;

    private void OnChanged()
    {
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
