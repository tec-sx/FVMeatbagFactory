using System.Numerics;
using System.Text.Json;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Rig;
using FVMeatbag.Graph;

namespace FVMeatbag.Core.Muscles;

/// <summary>
/// Where a template puts a muscle end, in body terms: the point <see cref="Along"/> the bone, pushed
/// to the skin in <see cref="Direction"/> and kept <see cref="Depth"/> of the way back from it, so
/// it sits under the surface on any body shape.
/// </summary>
/// <param name="Bone">Bone name or alias (<c>chest</c>), written for the left side.</param>
/// <param name="Along">0 at the bone's joint, 1 at the next joint of its chain.</param>
/// <param name="Direction">(front, left, up) of the body.</param>
/// <param name="Depth">How far back from the skin, 0 on it to 1 at the bone.</param>
public sealed record TemplateAttachment(string Bone, float Along, Vector3 Direction, float Depth);

/// <summary>One end pair of a template muscle.</summary>
public sealed record TemplateStrand(TemplateAttachment Origin, TemplateAttachment Insertion);

/// <summary>
/// A template muscle, written for the left side: one strand for a simple muscle, a fan of strands
/// for one with several attachments (pectoral, deltoid), hooks for broad pads several joints bend.
/// </summary>
/// <param name="Name">With its side: <c>Biceps_l</c>.</param>
/// <param name="Width">Belly radius across, relative to the room under the skin.</param>
/// <param name="Thickness">Belly radius towards the skin, relative to the room under the skin.</param>
public sealed record TemplateMuscle(
    string Name,
    IReadOnlyList<TemplateStrand> Strands,
    IReadOnlyList<TemplateAttachment> Hooks,
    float Width,
    float Thickness,
    float Belly,
    float Tendon,
    Vector3 Color,
    IReadOnlyList<MuscleDrive> Drives)
{
    /// <summary>The name without its side: <c>Biceps</c>.</summary>
    public string BaseName => Name.EndsWith("_l", StringComparison.OrdinalIgnoreCase) ? Name[..^2] : Name;

    /// <summary><c>Pectoral2_l</c> for a fan's second strand, <c>Biceps_l</c> for a single strand.</summary>
    public string StrandName(int strand) =>
        Strands.Count == 1 ? Name : $"{BaseName}{strand + 1}{Name[BaseName.Length..]}";
}

/// <summary>What <see cref="MuscleTemplate.Generate"/> did.</summary>
/// <param name="Missing">Bones the skeleton lacks; nothing is built when there are any.</param>
public sealed record MuscleTemplateReport(IReadOnlyList<string> Added, IReadOnlyList<string> Missing)
{
    public string Summary => Missing.Count == 0
        ? $"Added {Added.Count} muscle strands."
        : $"The skeleton lacks {string.Join(", ", Missing)}.";
}

/// <summary>
/// The human muscles of the Flicker Void tools for Mannequin skeletons, read from the embedded
/// MuscleTemplate.json. <see cref="Generate"/> places every muscle under the skin on the left,
/// mirrors it to the right and fits it all to the skin.
/// </summary>
public sealed class MuscleTemplate
{
    /// <summary>Clearance used when the skin can't be reached, relative to <see cref="BodyFrame.Unit"/>.</summary>
    private const float FallbackClearance = 0.05f;

    private static readonly Lazy<MuscleTemplate> s_default = new(LoadDefault);

    public MuscleTemplate(IEnumerable<TemplateMuscle> muscles, IReadOnlyDictionary<string, IReadOnlyList<string>> aliases, IEnumerable<string> required)
    {
        ArgumentNullException.ThrowIfNull(muscles);
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(required);
        Muscles = [.. muscles];
        Aliases = aliases;
        Required = [.. required];
    }

    public static MuscleTemplate Default => s_default.Value;

    public IReadOnlyList<TemplateMuscle> Muscles { get; }

    /// <summary>Template names for bones that differ between Mannequin generations: the first bone the skeleton has is used.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Aliases { get; }

    public IReadOnlyList<string> Required { get; }

    /// <summary>The template muscle by its name with or without side (<c>Biceps_l</c>, <c>Biceps</c>).</summary>
    public Maybe<TemplateMuscle> Find(string name) =>
        Muscles.FirstOrNone(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(m.BaseName, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The skeleton's bone for a template bone name or alias, or None.</summary>
    public Maybe<int> ResolveBone(string name, Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        var candidates = Aliases.TryGetValue(name, out var names) ? names : [name];
        return candidates.Select(skeleton.FindBone).FirstOrNone(b => b.HasValue).Map(b => b.Value);
    }

    /// <summary>The bones and aliases <paramref name="skeleton"/> lacks; empty when the template fits.</summary>
    public IReadOnlyList<string> MissingBones(Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        var missing = Required.Where(b => skeleton.FindBone(b).IsNone).ToList();
        missing.AddRange(Aliases.Keys.Where(a => ResolveBone(a, skeleton).IsNone && !missing.Contains(a)));
        return missing;
    }

    /// <summary>
    /// Builds every template muscle on the left, mirrors it to the right and fits all to
    /// <paramref name="skin"/>, replacing the muscles an earlier run made and keeping hand-made ones.
    /// Nothing changes when the skeleton lacks a bone the template needs.
    /// </summary>
    public MuscleTemplateReport Generate(BodyFrame body, SkinSurface skin, MuscleSet muscles)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(skin);
        ArgumentNullException.ThrowIfNull(muscles);
        var missing = MissingBones(body.Skeleton);
        if (missing.Count > 0)
        {
            return new MuscleTemplateReport([], missing);
        }
        foreach (var old in muscles.Muscles.Where(m => m.Template.HasValue).Select(m => m.Name).ToList())
        {
            muscles.Remove(old);
        }
        var added = new List<string>();
        foreach (var template in Muscles)
        {
            for (var strand = 0; strand < template.Strands.Count; strand++)
            {
                if (!Build(template, strand, body, skin).TryGetValue(out var left))
                {
                    continue;
                }
                foreach (var muscle in body.Mirror(left).Match(right => new[] { left, right }, () => [left]))
                {
                    muscles.Set(body.FitToSkin(muscle, skin));
                    added.Add(muscle.Name);
                }
            }
        }
        return new MuscleTemplateReport(added, []);
    }

    /// <summary>
    /// The muscle <paramref name="muscle"/> came from, built again on its side and fitted to the
    /// skin; None for a hand-made muscle or when its bones are missing.
    /// </summary>
    public Maybe<Muscle> Rebuild(Muscle muscle, BodyFrame body, SkinSurface skin)
    {
        ArgumentNullException.ThrowIfNull(muscle);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(skin);
        if (!muscle.Template.TryGetValue(out var source) || !Find(source.Muscle).TryGetValue(out var template))
        {
            return Maybe<Muscle>.None;
        }
        return Build(template, source.Strand, body, skin)
            .Bind(left => muscle.Side == BoneSide.Right ? body.Mirror(left) : left)
            .Map(m => body.FitToSkin(m, skin));
    }

    /// <summary>
    /// Strand <paramref name="strand"/> of <paramref name="template"/> on the left, before Fit to
    /// Skin: ends placed by rays to the skin, belly radii from the room around the belly point.
    /// </summary>
    public Maybe<Muscle> Build(TemplateMuscle template, int strand, BodyFrame body, SkinSurface skin)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(skin);
        if (strand < 0 || strand >= template.Strands.Count)
        {
            return Maybe<Muscle>.None;
        }
        var ends = template.Strands[strand];
        var hooks = template.Hooks.Select(h => Place(h, body, skin)).ToList();
        if (!Place(ends.Origin, body, skin).TryGetValue(out var origin)
            || !Place(ends.Insertion, body, skin).TryGetValue(out var insertion)
            || hooks.Any(h => h.IsNone))
        {
            return Maybe<Muscle>.None;
        }
        var clearance = skin.Distance(Vector3.Lerp(origin.Point, insertion.Point, template.Belly)).Match(d => d, () => body.Unit * FallbackClearance);
        // The thickness axis points where the template pushes both ends: out to the skin.
        var outward = body.Direction(ends.Origin.Direction + ends.Insertion.Direction, BoneSide.Left);
        var axis = insertion.Point - origin.Point;
        if (axis.LengthSquared() > 1e-12f)
        {
            axis = Vector3.Normalize(axis);
            outward -= Vector3.Dot(outward, axis) * axis;
        }
        if (outward.LengthSquared() < 1e-10f)
        {
            outward = body.Forward;
        }
        return new Muscle(
            template.StrandName(strand),
            template.Name,
            new MuscleSource(template.BaseName, strand),
            BoneSide.Left,
            Attach(origin, body),
            Attach(insertion, body),
            [.. hooks.Select(h => Attach(h.Value, body))],
            body.ToLocalDirection(origin.Bone, outward),
            clearance * template.Width,
            clearance * template.Thickness,
            template.Belly,
            template.Tendon,
            1f,
            0f,
            Maybe<IReadOnlyList<float>>.None,
            template.Color,
            template.Drives);
    }

    /// <summary>The rest-pose model-space point of a template attachment, and its bone.</summary>
    public Maybe<(int Bone, Vector3 Point)> Place(TemplateAttachment attachment, BodyFrame body, SkinSurface skin)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(skin);
        return ResolveBone(attachment.Bone, body.Skeleton).Map(bone =>
        {
            var start = body.BonePoint(bone, attachment.Along);
            var direction = body.Direction(attachment.Direction, BoneSide.Left);
            var point = skin.RayHit(start, direction).Match(hit => Vector3.Lerp(start, hit, 1f - attachment.Depth), () => start);
            return (bone, point);
        });
    }

    public static MuscleTemplate Load(Stream json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var colors = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("colors", out var colorList))
        {
            foreach (var color in colorList.EnumerateObject())
            {
                colors[color.Name] = Vector(color.Value);
            }
        }
        var aliases = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("aliases", out var aliasList))
        {
            foreach (var alias in aliasList.EnumerateObject())
            {
                aliases[alias.Name] = [.. alias.Value.EnumerateArray().Select(b => b.GetString() ?? "")];
            }
        }
        var required = root.TryGetProperty("required", out var requiredList)
            ? requiredList.EnumerateArray().Select(b => b.GetString() ?? "").ToList()
            : [];
        var muscles = root.GetProperty("muscles").EnumerateArray().Select(m => ReadMuscle(m, colors)).ToList();
        return new MuscleTemplate(muscles, aliases, required);
    }

    private static MuscleAttachment Attach((int Bone, Vector3 Point) placed, BodyFrame body) =>
        new(body.Skeleton.Bones[placed.Bone].Name, body.ToLocalPoint(placed.Bone, placed.Point));

    private static TemplateMuscle ReadMuscle(JsonElement muscle, Dictionary<string, Vector3> colors)
    {
        var name = muscle.GetProperty("name").GetString() ?? throw new InvalidDataException("A template muscle has no name.");
        var strands = muscle.GetProperty("strands").EnumerateArray()
            .Select(s => s.EnumerateArray().Select(ReadAttachment).ToList() is [var origin, var insertion]
                ? new TemplateStrand(origin, insertion)
                : throw new InvalidDataException($"A strand of template muscle {name} needs an origin and an insertion."))
            .ToList();
        var hooks = muscle.TryGetProperty("hooks", out var hookList) ? hookList.EnumerateArray().Select(ReadAttachment).ToList() : [];
        var color = muscle.TryGetProperty("color", out var colorName) && colors.TryGetValue(colorName.GetString() ?? "", out var c) ? c : new Vector3(0.75f, 0.2f, 0.2f);
        var drives = muscle.TryGetProperty("drives", out var driveList)
            ? driveList.EnumerateArray().Select(d => new MuscleDrive(
                d.GetProperty("role").GetString() ?? throw new InvalidDataException($"A drive of {name} has no role."),
                d.TryGetProperty("part", out var part) && !string.IsNullOrWhiteSpace(part.GetString()) ? part.GetString()! : Maybe<string>.None,
                Number(d, "weight", 1f))).ToList()
            : [];
        return new TemplateMuscle(
            name,
            strands,
            hooks,
            Number(muscle, "width", 0.85f),
            Number(muscle, "thickness", 0.85f),
            Number(muscle, "belly", Muscle.DefaultBelly),
            Number(muscle, "tendon", Muscle.DefaultTendon),
            color,
            drives);
    }

    private static TemplateAttachment ReadAttachment(JsonElement attachment) => new(
        attachment.GetProperty("bone").GetString() ?? throw new InvalidDataException("A template attachment has no bone."),
        Number(attachment, "along", 0f),
        attachment.TryGetProperty("direction", out var direction) ? Vector(direction) : Vector3.Zero,
        Number(attachment, "depth", 0.4f));

    private static float Number(JsonElement element, string name, float fallback) =>
        element.TryGetProperty(name, out var value) ? value.GetSingle() : fallback;

    private static Vector3 Vector(JsonElement values) =>
        values.EnumerateArray().Select(v => v.GetSingle()).ToArray() is [var x, var y, var z] ? new Vector3(x, y, z) : Vector3.Zero;

    private static MuscleTemplate LoadDefault()
    {
        using var stream = typeof(MuscleTemplate).Assembly.GetManifestResourceStream("FVMeatbag.Core.Muscles.MuscleTemplate.json")
            ?? throw new InvalidOperationException("MuscleTemplate.json is not embedded.");
        return Load(stream);
    }
}
