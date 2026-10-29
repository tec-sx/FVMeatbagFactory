using System.Text.RegularExpressions;

namespace FVMeatbag.Core.Rig;

public enum BoneSide
{
    Center,
    Left,
    Right,
}

/// <summary>Reads and rewrites the side marker in bone names.</summary>
public static partial class BoneNames
{
    /// <summary>
    /// Reads the side from common naming schemes: Blender/Rigify (<c>hand.L</c>, <c>hand_R</c>),
    /// Maya/UE (<c>L_hand</c>, <c>hand_l</c>) and Mixamo (<c>LeftHand</c>, <c>mixamorig:RightArm</c>).
    /// </summary>
    public static BoneSide SideOf(string boneName)
    {
        ArgumentNullException.ThrowIfNull(boneName);
        if (LeftPattern().IsMatch(boneName))
        {
            return BoneSide.Left;
        }
        return RightPattern().IsMatch(boneName) ? BoneSide.Right : BoneSide.Center;
    }

    /// <summary>
    /// The same name for the other side (<c>bicep_l</c> → <c>bicep_r</c>, <c>hand.L</c> →
    /// <c>hand.R</c>, <c>LeftHand</c> → <c>RightHand</c>), keeping the original's case. Center names
    /// come back unchanged.
    /// </summary>
    public static string Mirror(string boneName)
    {
        ArgumentNullException.ThrowIfNull(boneName);
        var (from, to) = SideOf(boneName) switch
        {
            BoneSide.Left => ('l', 'r'),
            BoneSide.Right => ('r', 'l'),
            _ => ('\0', '\0'),
        };
        if (from == '\0')
        {
            return boneName;
        }
        var word = from == 'l' ? WordLeft().Match(boneName) : WordRight().Match(boneName);
        if (word.Success)
        {
            var replacement = from == 'l' ? "right" : "left";
            return boneName[..word.Index] + MatchCase(replacement, word.Value) + boneName[(word.Index + word.Length)..];
        }
        var marker = (from == 'l' ? LeftPattern() : RightPattern()).Match(boneName);
        var letter = boneName.IndexOf(marker.Value.First(c => char.ToLowerInvariant(c) == from), marker.Index);
        var swapped = char.IsUpper(boneName[letter]) ? char.ToUpperInvariant(to) : to;
        return string.Concat(boneName.AsSpan(0, letter), [swapped], boneName.AsSpan(letter + 1));
    }

    /// <summary>
    /// Splits a name into lower-case words at separators, camel case and digits:
    /// <c>hipFlexor_02_L</c> → <c>hip, flexor, 02, l</c>.
    /// </summary>
    public static IReadOnlyList<string> Words(string boneName)
    {
        ArgumentNullException.ThrowIfNull(boneName);
        return [.. WordPattern().Matches(boneName).Select(m => m.Value.ToLowerInvariant())];
    }

    private static string MatchCase(string replacement, string original) =>
        original.All(char.IsUpper) ? replacement.ToUpperInvariant()
        : char.IsUpper(original[0]) ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
        : replacement;

    [GeneratedRegex(@"([._\-\s]L$)|(^L[._\-])|([._\-]L[._\-])|(left)", RegexOptions.IgnoreCase)]
    private static partial Regex LeftPattern();

    [GeneratedRegex(@"([._\-\s]R$)|(^R[._\-])|([._\-]R[._\-])|(right)", RegexOptions.IgnoreCase)]
    private static partial Regex RightPattern();

    [GeneratedRegex("left", RegexOptions.IgnoreCase)]
    private static partial Regex WordLeft();

    [GeneratedRegex("right", RegexOptions.IgnoreCase)]
    private static partial Regex WordRight();

    [GeneratedRegex(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+")]
    private static partial Regex WordPattern();
}
