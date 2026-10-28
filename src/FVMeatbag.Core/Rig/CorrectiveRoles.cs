using System.Text.Json;
using System.Text.Json.Serialization;
using FVMeatbag.Graph;

namespace FVMeatbag.Core.Rig;

public enum BodyRegion
{
    Torso,
    Neck,
    Arm,
    Leg,
    Face,
}

/// <summary>
/// What a corrective bone stands for: a muscle (biceps), a joint area (knee) or a tendon. Muscle
/// templates and the solver address correctives by role, so any naming scheme works once its
/// bones are tagged.
/// </summary>
/// <param name="Id">Stable id, also the words that name it in bone names (<c>hip_flexor</c>).</param>
/// <param name="Parent">Manny bone without side (<c>upperarm</c>) that the corrective usually hangs under.</param>
/// <param name="Sided">False for roles on the center line, such as the abdominals.</param>
public sealed record CorrectiveRole(string Id, string Name, BodyRegion Region, string Parent, bool Sided, IReadOnlyList<string> Aliases);

/// <summary>A corrective bone's role, side and optional part, e.g. biceps, left, "hi".</summary>
/// <param name="Part">Distinguishes several bones of one role (<c>hi</c>, <c>lo</c>, <c>inner</c>); None for a single bone.</param>
public readonly record struct CorrectiveTag(string Role, BoneSide Side, Maybe<string> Part)
{
    /// <summary>Canonical UE-style name: <c>bicep_hi_l</c>.</summary>
    public string CanonicalName => Role + Part.Match(p => "_" + p, () => "") + Side switch
    {
        BoneSide.Left => "_l",
        BoneSide.Right => "_r",
        _ => "",
    };

    public CorrectiveTag Mirrored => this with
    {
        Side = Side switch
        {
            BoneSide.Left => BoneSide.Right,
            BoneSide.Right => BoneSide.Left,
            _ => BoneSide.Center,
        },
    };

    public override string ToString() => CanonicalName;
}

/// <summary>The list of known roles. <see cref="Default"/> is read from the embedded CorrectiveRoles.json.</summary>
public sealed class CorrectiveRoleCatalog
{
    private static readonly HashSet<string> SideWords = new(StringComparer.Ordinal) { "l", "r", "left", "right" };
    private static readonly Lazy<CorrectiveRoleCatalog> s_default = new(LoadDefault);

    private readonly CorrectiveRole[] _roles;
    private readonly Dictionary<string, CorrectiveRole> _byId;

    // Every spelling as words, longest first, so "achilles_tendon" wins over "achilles".
    private readonly (string[] Words, CorrectiveRole Role)[] _spellings;

    public CorrectiveRoleCatalog(IEnumerable<CorrectiveRole> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        _roles = [.. roles];
        _byId = _roles.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        _spellings = [.. _roles
            .SelectMany(r => r.Aliases.Prepend(r.Id).Select(s => (Words: Split(s), Role: r)))
            .OrderByDescending(s => s.Words.Length)
            .ThenByDescending(s => s.Words.Sum(w => w.Length))];
    }

    public static CorrectiveRoleCatalog Default => s_default.Value;

    public IReadOnlyList<CorrectiveRole> Roles => _roles;

    public Maybe<CorrectiveRole> Find(string id) => _byId.TryGetValue(id, out var role) ? role : Maybe<CorrectiveRole>.None;

    /// <summary>
    /// Guesses the tag of a bone from its name: <c>bicep_l</c> → biceps, left;
    /// <c>Quad_Upper_R</c> → quadriceps, right, part "upper"; <c>achilles_tendon_l</c> → Achilles
    /// tendon, left. Words left over after the role and side become the part. None when no role matches.
    /// </summary>
    public Maybe<CorrectiveTag> Suggest(string boneName)
    {
        ArgumentNullException.ThrowIfNull(boneName);
        var words = BoneNames.Words(boneName).ToList();
        foreach (var (spelling, role) in _spellings)
        {
            var at = IndexOf(words, spelling);
            if (at < 0)
            {
                continue;
            }
            var rest = words.Where((_, i) => i < at || i >= at + spelling.Length).Where(w => !SideWords.Contains(w)).ToArray();
            var side = role.Sided ? BoneNames.SideOf(boneName) : BoneSide.Center;
            var part = rest.Length == 0 ? Maybe<string>.None : string.Join('_', rest);
            return new CorrectiveTag(role.Id, side, part);
        }
        return Maybe<CorrectiveTag>.None;
    }

    public static CorrectiveRoleCatalog Load(Stream json)
    {
        var document = JsonSerializer.Deserialize<CatalogDocument>(json, Options)
            ?? throw new InvalidDataException("The corrective role catalog is empty.");
        return new CorrectiveRoleCatalog((document.Roles ?? []).Select(r => new CorrectiveRole(
            r.Id ?? throw new InvalidDataException("A corrective role has no id."),
            r.Name ?? r.Id,
            r.Region,
            r.Parent ?? MannySkeleton.Root,
            r.Sided ?? true,
            r.Aliases ?? [])));
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static CorrectiveRoleCatalog LoadDefault()
    {
        using var stream = typeof(CorrectiveRoleCatalog).Assembly.GetManifestResourceStream("FVMeatbag.Core.Rig.CorrectiveRoles.json")
            ?? throw new InvalidOperationException("CorrectiveRoles.json is not embedded.");
        return Load(stream);
    }

    private static string[] Split(string spelling) => [.. BoneNames.Words(spelling)];

    private static int IndexOf(List<string> words, string[] spelling)
    {
        for (var i = 0; i + spelling.Length <= words.Count; i++)
        {
            if (spelling.Select((w, j) => words[i + j] == w).All(m => m))
            {
                return i;
            }
        }
        return -1;
    }

    private sealed class CatalogDocument
    {
        public List<RoleDocument>? Roles { get; set; }
    }

    private sealed class RoleDocument
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public BodyRegion Region { get; set; }
        public string? Parent { get; set; }
        public bool? Sided { get; set; }
        public List<string>? Aliases { get; set; }
    }
}
