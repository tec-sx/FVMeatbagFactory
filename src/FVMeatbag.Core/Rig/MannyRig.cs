using FVMeatbag.Core.Import;
using FVMeatbag.Graph;

namespace FVMeatbag.Core.Rig;

public enum MannyProblemKind
{
    /// <summary>A required core bone is not in the skeleton.</summary>
    Missing,

    /// <summary>A core bone hangs under the wrong parent.</summary>
    WrongParent,

    /// <summary>Two bones share a name when compared case-insensitively, as UE does.</summary>
    DuplicateName,

    /// <summary>A bone other than <c>root</c> has no parent.</summary>
    ExtraRoot,
}

public sealed record MannyProblem(MannyProblemKind Kind, string Bone, string Message);

/// <summary>The result of checking a skeleton against <see cref="MannySkeleton"/>.</summary>
/// <param name="Rig">The checked rig; None when there are problems.</param>
public sealed record MannyCheck(IReadOnlyList<MannyProblem> Problems, Maybe<MannyRig> Rig)
{
    public bool IsConformant => Rig.HasValue;

    /// <summary>One line for the UI.</summary>
    public string Summary => Rig.Match(
        r => $"UE5 Manny skeleton: {r.CoreCount} core bones, {r.Extras.Count} extras",
        () => $"Not a UE5 Manny skeleton: {Problems.Count} problem{(Problems.Count == 1 ? "" : "s")}");
}

/// <summary>
/// A skeleton that matches the UE5 Manny hierarchy, split into core bones (the Manny body) and
/// extras (correctives, face, sockets). Built by <see cref="Check"/>.
/// </summary>
/// <remarks>A snapshot: renaming a bone bumps <see cref="Skeleton.Version"/>, and the rig must be checked again.</remarks>
public sealed class MannyRig
{
    private readonly Dictionary<string, int> _core;
    private readonly int[] _extras;
    private readonly bool[] _isExtra;

    private MannyRig(Skeleton skeleton, Dictionary<string, int> core, bool[] isExtra)
    {
        Skeleton = skeleton;
        SkeletonVersion = skeleton.Version;
        _core = core;
        _isExtra = isExtra;
        _extras = [.. Enumerable.Range(0, isExtra.Length).Where(i => isExtra[i])];
    }

    public Skeleton Skeleton { get; }

    /// <summary>The <see cref="Skeleton.Version"/> this rig was checked at.</summary>
    public long SkeletonVersion { get; }

    public int CoreCount => _core.Count;

    /// <summary>Indices of the bones that are not part of the Manny body, in skeleton order.</summary>
    public IReadOnlyList<int> Extras => _extras;

    public bool IsExtra(int bone) => _isExtra[bone];

    /// <summary>The index of a core bone by its Manny name; None for an optional bone the skeleton lacks.</summary>
    public Maybe<int> Core(string mannyName) => _core.TryGetValue(mannyName, out var index) ? index : Maybe<int>.None;

    /// <summary>The nearest core bone at or above <paramref name="bone"/>: the bone an extra follows.</summary>
    public int DriverOf(int bone)
    {
        while (_isExtra[bone])
        {
            bone = Skeleton.Bones[bone].Parent;
        }
        return bone;
    }

    public static MannyCheck Check(Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        var bones = skeleton.Bones;
        var problems = new List<MannyProblem>();

        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < bones.Count; i++)
        {
            if (!byName.TryAdd(bones[i].Name, i))
            {
                problems.Add(new(MannyProblemKind.DuplicateName, bones[i].Name,
                    $"{bones[i].Name} appears more than once (names are compared ignoring case, as in UE)."));
            }
        }

        var core = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var manny in MannySkeleton.Bones)
        {
            if (!byName.TryGetValue(manny.Name, out var index))
            {
                if (manny.Kind == MannyBoneKind.Required)
                {
                    problems.Add(new(MannyProblemKind.Missing, manny.Name, $"{manny.Name} is missing."));
                }
                continue;
            }
            core[manny.Name] = index;
            var parent = bones[index].Parent;
            var actual = parent < 0 ? Maybe<string>.None : bones[parent].Name;
            var matches = manny.Parent.Match(
                expected => actual.Match(a => string.Equals(a, expected, StringComparison.OrdinalIgnoreCase), () => false),
                () => actual.IsNone);
            if (!matches)
            {
                var expectedText = manny.Parent.Match(p => $"a child of {p}", () => "the root");
                var actualText = actual.Match(a => $"it is under {a}", () => "it has no parent");
                problems.Add(new(MannyProblemKind.WrongParent, bones[index].Name, $"{bones[index].Name} should be {expectedText}, but {actualText}."));
            }
        }

        var isExtra = new bool[bones.Count];
        for (var i = 0; i < bones.Count; i++)
        {
            isExtra[i] = !MannySkeleton.IsCore(bones[i].Name);
            if (isExtra[i] && bones[i].Parent < 0)
            {
                problems.Add(new(MannyProblemKind.ExtraRoot, bones[i].Name,
                    $"{bones[i].Name} has no parent; extra bones must hang below a core bone."));
            }
        }

        return problems.Count > 0
            ? new MannyCheck(problems, Maybe<MannyRig>.None)
            : new MannyCheck(problems, new MannyRig(skeleton, core, isExtra));
    }
}
