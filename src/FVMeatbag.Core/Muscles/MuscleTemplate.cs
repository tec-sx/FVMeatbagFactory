using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FVMeatbag.Core.Rig;
using FVMeatbag.Graph;

namespace FVMeatbag.Core.Muscles;

/// <summary>Where a template puts one muscle end, in body terms.</summary>
/// <param name="Bone">Manny bone; without its side suffix for sided bones (<c>upperarm</c>).</param>
/// <param name="Toward">Second bone the point slides towards, or None to stay at <see cref="Bone"/>.</param>
/// <param name="Along">0 at <see cref="Bone"/>, 1 at <see cref="Toward"/>.</param>
/// <param name="Offset">(forward, out, up) in <see cref="BodyFrame.Unit"/>s; "out" points away from the center line.</param>
public sealed record TemplateAnchor(string Bone, Maybe<string> Toward, float Along, Vector3 Offset);

/// <param name="Id">Stable id, also the muscle's name before its side suffix (<c>biceps</c> → <c>biceps_l</c>).</param>
/// <param name="Radius">Belly half-width in <see cref="BodyFrame.Unit"/>s.</param>
/// <param name="Facing">(forward, out, up) direction the outer surface faces.</param>
public sealed record MuscleTemplateEntry(
    string Id,
    string Name,
    BodyRegion Region,
    bool Sided,
    MuscleShape Shape,
    MuscleAction Action,
    TemplateAnchor Origin,
    TemplateAnchor Insertion,
    Vector3 Facing,
    float Radius,
    float Flatness,
    float Thickness,
    float Volume,
    Vector2 Tendons,
    IReadOnlyList<MuscleDrive> Drives);

/// <summary>What <see cref="MuscleTemplate.Apply"/> did.</summary>
/// <param name="Skipped">Muscles not added, each with the reason.</param>
public sealed record MuscleTemplateReport(IReadOnlyList<string> Added, IReadOnlyList<string> Skipped)
{
    public string Summary => Skipped.Count == 0
        ? $"Added {Added.Count} muscles."
        : $"Added {Added.Count} muscles, skipped {Skipped.Count}.";
}

/// <summary>
/// Muscles placed by Manny bone names. <see cref="Default"/> is read from the embedded
/// MuscleTemplate.json and covers the body's main muscles; each is linked to the corrective roles it
/// drives.
/// </summary>
public sealed class MuscleTemplate
{
    private static readonly Lazy<MuscleTemplate> s_default = new(LoadDefault);

    public MuscleTemplate(IEnumerable<MuscleTemplateEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = [.. entries];
    }

    public static MuscleTemplate Default => s_default.Value;

    public IReadOnlyList<MuscleTemplateEntry> Entries { get; }

    public Maybe<MuscleTemplateEntry> Find(string id) =>
        Entries.FirstOrNone(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adds every template muscle (both sides of sided ones) to <paramref name="muscles"/>. Muscles
    /// whose name is already taken are left alone unless <paramref name="replace"/> is set; muscles
    /// whose bones the skeleton lacks are skipped and reported.
    /// </summary>
    public MuscleTemplateReport Apply(BodyFrame body, MuscleSet muscles, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(muscles);
        var added = new List<string>();
        var skipped = new List<string>();
        foreach (var entry in Entries)
        {
            foreach (var side in entry.Sided ? (BoneSide[])[BoneSide.Left, BoneSide.Right] : [BoneSide.Center])
            {
                var name = MuscleName(entry, side);
                if (!replace && muscles.Find(name).HasValue)
                {
                    skipped.Add($"{name}: already there");
                    continue;
                }
                switch (Build(entry, side, body))
                {
                    case { HasValue: true } built:
                        muscles.Set(built.Value);
                        added.Add(name);
                        break;
                    default:
                        skipped.Add($"{name}: the skeleton lacks {MissingBone(entry, side, body)}");
                        break;
                }
            }
        }
        return new MuscleTemplateReport(added, skipped);
    }

    /// <summary>The muscle <paramref name="entry"/> makes on <paramref name="side"/>, or None if a bone is missing.</summary>
    public static Maybe<Muscle> Build(MuscleTemplateEntry entry, BoneSide side, BodyFrame body)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(body);
        var origin = Resolve(entry.Origin, side, body);
        var insertion = Resolve(entry.Insertion, side, body);
        if (!origin.TryGetValue(out var o) || !insertion.TryGetValue(out var i) || !body.Skeleton.FindBone(o.Bone).TryGetValue(out var originBone))
        {
            return Maybe<Muscle>.None;
        }
        var facing = body.Direction(entry.Facing, side);
        return new Muscle(
            MuscleName(entry, side),
            entry.Id,
            side,
            o,
            i,
            body.ToLocalDirection(originBone, facing.LengthSquared() > 1e-12f ? facing : body.Forward),
            entry.Shape,
            entry.Action,
            entry.Radius * body.Unit,
            entry.Flatness,
            entry.Thickness,
            entry.Volume,
            entry.Tendons,
            entry.Drives);
    }

    public static string MuscleName(MuscleTemplateEntry entry, BoneSide side)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Id + Suffix(side);
    }

    /// <summary>The skeleton's name for a template bone on <paramref name="side"/>: core center bones as given, others with the side suffix.</summary>
    public static string BoneName(string templateBone, BoneSide side)
    {
        ArgumentNullException.ThrowIfNull(templateBone);
        return MannySkeleton.IsCore(templateBone) ? templateBone : templateBone + Suffix(side);
    }

    public static MuscleTemplate Load(Stream json)
    {
        var document = JsonSerializer.Deserialize<TemplateDocument>(json, Options)
            ?? throw new InvalidDataException("The muscle template is empty.");
        return new MuscleTemplate((document.Muscles ?? []).Select(m => m.ToEntry()));
    }

    private static string Suffix(BoneSide side) => side switch
    {
        BoneSide.Left => "_l",
        BoneSide.Right => "_r",
        _ => "",
    };

    private static Maybe<MuscleAttachment> Resolve(TemplateAnchor anchor, BoneSide side, BodyFrame body)
    {
        var skeleton = body.Skeleton;
        if (!skeleton.FindBone(BoneName(anchor.Bone, side)).TryGetValue(out var bone))
        {
            return Maybe<MuscleAttachment>.None;
        }
        var start = skeleton.WorldRest[bone].Translation;
        var point = start;
        if (anchor.Toward.TryGetValue(out var toward))
        {
            if (!skeleton.FindBone(BoneName(toward, side)).TryGetValue(out var towardBone))
            {
                return Maybe<MuscleAttachment>.None;
            }
            point = Vector3.Lerp(start, skeleton.WorldRest[towardBone].Translation, anchor.Along);
        }
        point += body.Direction(anchor.Offset, side) * body.Unit;
        return new MuscleAttachment(skeleton.Bones[bone].Name, body.ToLocalPoint(bone, point));
    }

    private static string MissingBone(MuscleTemplateEntry entry, BoneSide side, BodyFrame body) =>
        new[] { entry.Origin, entry.Insertion }
            .SelectMany(a => a.Toward.Match(t => new[] { a.Bone, t }, () => [a.Bone]))
            .Select(b => BoneName(b, side))
            .FirstOrDefault(b => body.Skeleton.FindBone(b).IsNone) ?? "a bone";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static MuscleTemplate LoadDefault()
    {
        using var stream = typeof(MuscleTemplate).Assembly.GetManifestResourceStream("FVMeatbag.Core.Muscles.MuscleTemplate.json")
            ?? throw new InvalidOperationException("MuscleTemplate.json is not embedded.");
        return Load(stream);
    }

    private sealed class TemplateDocument
    {
        public List<EntryDocument>? Muscles { get; set; }
    }

    private sealed class EntryDocument
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public BodyRegion Region { get; set; }
        public bool? Sided { get; set; }
        public MuscleShape Shape { get; set; }
        public MuscleAction Action { get; set; }
        public AnchorDocument? Origin { get; set; }
        public AnchorDocument? Insertion { get; set; }
        public float[]? Facing { get; set; }
        public float Radius { get; set; }
        public float? Flatness { get; set; }
        public float Thickness { get; set; }
        public float? Volume { get; set; }
        public float[]? Tendons { get; set; }
        public List<DriveDocument>? Drives { get; set; }

        public MuscleTemplateEntry ToEntry()
        {
            var id = Id ?? throw new InvalidDataException("A template muscle has no id.");
            return new MuscleTemplateEntry(
                id,
                Name ?? id,
                Region,
                Sided ?? true,
                Shape,
                Action,
                (Origin ?? throw new InvalidDataException($"Template muscle {id} has no origin.")).ToAnchor(),
                (Insertion ?? throw new InvalidDataException($"Template muscle {id} has no insertion.")).ToAnchor(),
                Vector(Facing),
                Radius,
                Flatness ?? 1f,
                Thickness,
                Volume ?? 1f,
                Tendons is [var a, var b] ? new Vector2(a, b) : new Vector2(0.1f, 0.1f),
                [.. (Drives ?? []).Select(d => d.ToDrive())]);
        }
    }

    private sealed class AnchorDocument
    {
        public string? Bone { get; set; }
        public string? Toward { get; set; }
        public float Along { get; set; }
        public float[]? Offset { get; set; }

        public TemplateAnchor ToAnchor() => new(
            Bone ?? throw new InvalidDataException("A template anchor has no bone."),
            Maybe.From(Toward),
            Along,
            Vector(Offset));
    }

    private sealed class DriveDocument
    {
        public string? Role { get; set; }
        public string? Part { get; set; }
        public float? Weight { get; set; }

        public MuscleDrive ToDrive() => new(
            Role ?? throw new InvalidDataException("A muscle drive has no role."),
            string.IsNullOrWhiteSpace(Part) ? Maybe<string>.None : Part,
            Weight ?? 1f);
    }

    private static Vector3 Vector(float[]? values) => values is [var x, var y, var z] ? new Vector3(x, y, z) : Vector3.Zero;
}
