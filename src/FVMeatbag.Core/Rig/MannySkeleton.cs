using FVMeatbag.Graph;

namespace FVMeatbag.Core.Rig;

public enum MannyBoneKind
{
    /// <summary>Every conforming skeleton has it, under the listed parent.</summary>
    Required,

    /// <summary>May be missing (twist and IK bones); when present it must sit under the listed parent.</summary>
    Optional,
}

/// <param name="Parent">The Manny parent, or None for <c>root</c>.</param>
public sealed record MannyBone(string Name, Maybe<string> Parent, MannyBoneKind Kind);

/// <summary>
/// The UE5 Mannequin ("Manny") body skeleton: the names and hierarchy the whole app relies on.
/// Muscle templates, corrective roles and the UE export address bones by these names.
/// </summary>
/// <remarks>
/// <para>
/// Only the body is fixed. Any other bone (Manny's own correctives, the user's correctives, face
/// bones, attachment sockets) is an <em>extra</em>: allowed anywhere below the core, never as a
/// parent of a core bone. Names compare case-insensitively, like UE's FName.
/// </para>
/// <para>Written from the UE 5 SK_Mannequin hierarchy; it should be checked against a real export.</para>
/// </remarks>
public static class MannySkeleton
{
    public const string Root = "root";

    private static readonly MannyBone[] s_bones = Build();
    private static readonly Dictionary<string, MannyBone> s_byName = s_bones.ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>All core bones, parents before children.</summary>
    public static IReadOnlyList<MannyBone> Bones => s_bones;

    public static Maybe<MannyBone> Find(string name) => s_byName.TryGetValue(name, out var bone) ? bone : Maybe<MannyBone>.None;

    /// <summary>True if <paramref name="name"/> is a core Manny bone (required or optional).</summary>
    public static bool IsCore(string name) => s_byName.ContainsKey(name);

    /// <summary>
    /// The name without its UE side suffix: <c>upperarm_l</c> → <c>upperarm</c>. Corrective roles use
    /// it to say which bone they usually hang under, whatever the side.
    /// </summary>
    public static string BaseName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.EndsWith("_l", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_r", StringComparison.OrdinalIgnoreCase)
            ? name[..^2]
            : name;
    }

    private static MannyBone[] Build()
    {
        var bones = new List<MannyBone>();
        void Add(string name, string? parent, MannyBoneKind kind = MannyBoneKind.Required) =>
            bones.Add(new MannyBone(name, Maybe.From(parent), kind));

        Add(Root, null);
        Add("pelvis", Root);
        Add("spine_01", "pelvis");
        Add("spine_02", "spine_01");
        Add("spine_03", "spine_02");
        Add("spine_04", "spine_03");
        Add("spine_05", "spine_04");
        Add("neck_01", "spine_05");
        Add("neck_02", "neck_01");
        Add("head", "neck_02");

        foreach (var s in (ReadOnlySpan<string>)["_l", "_r"])
        {
            Add("clavicle" + s, "spine_05");
            Add("upperarm" + s, "clavicle" + s);
            Add("upperarm_twist_01" + s, "upperarm" + s, MannyBoneKind.Optional);
            Add("upperarm_twist_02" + s, "upperarm" + s, MannyBoneKind.Optional);
            Add("lowerarm" + s, "upperarm" + s);
            Add("lowerarm_twist_01" + s, "lowerarm" + s, MannyBoneKind.Optional);
            Add("lowerarm_twist_02" + s, "lowerarm" + s, MannyBoneKind.Optional);
            Add("hand" + s, "lowerarm" + s);
            Add("thumb_01" + s, "hand" + s);
            Add("thumb_02" + s, "thumb_01" + s);
            Add("thumb_03" + s, "thumb_02" + s);
            foreach (var finger in (ReadOnlySpan<string>)["index", "middle", "ring", "pinky"])
            {
                Add($"{finger}_metacarpal{s}", "hand" + s);
                Add($"{finger}_01{s}", $"{finger}_metacarpal{s}");
                Add($"{finger}_02{s}", $"{finger}_01{s}");
                Add($"{finger}_03{s}", $"{finger}_02{s}");
            }
        }

        foreach (var s in (ReadOnlySpan<string>)["_l", "_r"])
        {
            Add("thigh" + s, "pelvis");
            Add("thigh_twist_01" + s, "thigh" + s, MannyBoneKind.Optional);
            Add("thigh_twist_02" + s, "thigh" + s, MannyBoneKind.Optional);
            Add("calf" + s, "thigh" + s);
            Add("calf_twist_01" + s, "calf" + s, MannyBoneKind.Optional);
            Add("calf_twist_02" + s, "calf" + s, MannyBoneKind.Optional);
            Add("foot" + s, "calf" + s);
            Add("ball" + s, "foot" + s);
        }

        Add("ik_foot_root", Root, MannyBoneKind.Optional);
        Add("ik_foot_l", "ik_foot_root", MannyBoneKind.Optional);
        Add("ik_foot_r", "ik_foot_root", MannyBoneKind.Optional);
        Add("ik_hand_root", Root, MannyBoneKind.Optional);
        Add("ik_hand_gun", "ik_hand_root", MannyBoneKind.Optional);
        Add("ik_hand_l", "ik_hand_gun", MannyBoneKind.Optional);
        Add("ik_hand_r", "ik_hand_gun", MannyBoneKind.Optional);
        Add("interaction", Root, MannyBoneKind.Optional);
        Add("center_of_mass", Root, MannyBoneKind.Optional);
        return [.. bones];
    }
}
