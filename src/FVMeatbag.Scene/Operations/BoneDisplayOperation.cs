using System.Numerics;
using System.Text.RegularExpressions;
using FVMeatbag.Core.Import;
using FVMeatbag.Graph;

namespace FVMeatbag.Scene.Operations;

/// <summary>How bones are colored when not selected.</summary>
public enum BoneColorMode
{
    /// <summary>The Maya rigging convention: left side blue, right side red, center yellow.</summary>
    BySide,

    /// <summary>Maya's default joint color for every bone.</summary>
    Uniform,
}

/// <summary>A colored overlay line, in world space. Colors are display (sRGB) values.</summary>
public readonly record struct ColoredLine(Vector3 From, Vector3 To, Vector3 Color);

/// <summary>Maya's viewport colors for joints.</summary>
public static class MayaBoneColors
{
    /// <summary>Default joint color.</summary>
    public static readonly Vector3 Default = new(0.30f, 0.45f, 1.00f);

    /// <summary>Lead selection (light green).</summary>
    public static readonly Vector3 Selected = new(0.26f, 1.00f, 0.64f);

    /// <summary>Rig convention for the left side (blue), right side (red) and center (yellow).</summary>
    public static readonly Vector3 Left = new(0.20f, 0.40f, 1.00f);
    public static readonly Vector3 Right = new(1.00f, 0.22f, 0.22f);
    public static readonly Vector3 Center = new(1.00f, 0.90f, 0.20f);
}

public enum BoneSide
{
    Center,
    Left,
    Right,
}

public struct BoneDisplayInput
{
    public Maybe<Skeleton> Skeleton;

    /// <summary>Model-space bone transforms of the current pose; empty means the rest pose.</summary>
    public ReadOnlyMemory<Matrix4x4> BoneWorld;

    public Matrix4x4 World;
    public bool Visible;

    /// <summary>Scales joint spheres and bone width; 1 is the default size.</summary>
    public float BoneSize;

    public BoneColorMode ColorMode;
    public Maybe<int> SelectedBone;
}

public struct BoneDisplayOutput
{
    /// <summary>Lines that draw the skeleton; empty when hidden.</summary>
    public ReadOnlyMemory<ColoredLine> Lines;

    /// <summary>World-space joint centers, indexed like the skeleton's bones; empty when hidden.</summary>
    public ReadOnlyMemory<Vector3> Joints;

    /// <summary>World-space radius of the joint spheres.</summary>
    public float JointRadius;
}

/// <summary>
/// Draws a skeleton the way Maya does: a wire sphere (three circles) on every joint and a wire
/// pyramid from each joint to each child. Joints without children only get their sphere.
/// </summary>
public sealed partial class BoneDisplayOperation : Operation<BoneDisplayInput, BoneDisplayOutput>
{
    private const int CircleSegments = 16;

    /// <summary>Joint radius at size 1, as a fraction of the skeleton's largest extent.</summary>
    public const float RadiusPerExtent = 0.008f;

    public override void Execute(in BoneDisplayInput input, ref BoneDisplayOutput output, EvaluationContext context)
    {
        if (!input.Visible || !input.Skeleton.TryGetValue(out var skeleton) || skeleton.Count == 0)
        {
            output.Lines = ReadOnlyMemory<ColoredLine>.Empty;
            output.Joints = ReadOnlyMemory<Vector3>.Empty;
            output.JointRadius = 0f;
            return;
        }

        var world = input.World;
        var posed = input.BoneWorld.Span;
        var joints = new Vector3[skeleton.Count];
        for (var i = 0; i < joints.Length; i++)
        {
            var bone = posed.Length == joints.Length ? posed[i] : skeleton.WorldRest[i];
            joints[i] = Vector3.Transform(bone.Translation, world);
        }
        var bounds = BoundingBox.FromPoints(joints);
        var extent = MathF.Max(bounds.Size.X, MathF.Max(bounds.Size.Y, bounds.Size.Z));
        var radius = MathF.Max(extent * RadiusPerExtent, 1e-4f) * MathF.Max(input.BoneSize, 0.01f);

        var lines = new List<ColoredLine>(skeleton.Count * (CircleSegments * 3 + 8));
        var selected = input.SelectedBone.OrDefault(-1);
        for (var bone = 0; bone < skeleton.Count; bone++)
        {
            var color = bone == selected ? MayaBoneColors.Selected : ColorOf(skeleton.Bones[bone].Name, input.ColorMode);
            AddSphere(lines, joints[bone], radius, color);
            foreach (var child in skeleton.ChildrenOf(bone))
            {
                AddBone(lines, joints[bone], joints[child], radius, color);
            }
        }
        output.Lines = lines.ToArray();
        output.Joints = joints;
        output.JointRadius = radius;
    }

    public static Vector3 ColorOf(string boneName, BoneColorMode mode) => mode switch
    {
        BoneColorMode.Uniform => MayaBoneColors.Default,
        _ => SideOf(boneName) switch
        {
            BoneSide.Left => MayaBoneColors.Left,
            BoneSide.Right => MayaBoneColors.Right,
            _ => MayaBoneColors.Center,
        },
    };

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

    [GeneratedRegex(@"([._\-\s]L$)|(^L[._\-])|([._\-]L[._\-])|(left)", RegexOptions.IgnoreCase)]
    private static partial Regex LeftPattern();

    [GeneratedRegex(@"([._\-\s]R$)|(^R[._\-])|([._\-]R[._\-])|(right)", RegexOptions.IgnoreCase)]
    private static partial Regex RightPattern();

    private static void AddSphere(List<ColoredLine> lines, Vector3 center, float radius, Vector3 color)
    {
        AddCircle(lines, center, Vector3.UnitX * radius, Vector3.UnitY * radius, color);
        AddCircle(lines, center, Vector3.UnitY * radius, Vector3.UnitZ * radius, color);
        AddCircle(lines, center, Vector3.UnitZ * radius, Vector3.UnitX * radius, color);
    }

    private static void AddCircle(List<ColoredLine> lines, Vector3 center, Vector3 u, Vector3 v, Vector3 color)
    {
        var previous = center + u;
        for (var i = 1; i <= CircleSegments; i++)
        {
            var angle = 2 * MathF.PI * i / CircleSegments;
            var point = center + (u * MathF.Cos(angle)) + (v * MathF.Sin(angle));
            lines.Add(new ColoredLine(previous, point, color));
            previous = point;
        }
    }

    /// <summary>A four-sided wire pyramid from the parent joint's sphere to the child joint.</summary>
    private static void AddBone(List<ColoredLine> lines, Vector3 from, Vector3 to, float radius, Vector3 color)
    {
        var axis = to - from;
        var length = axis.Length();
        if (length <= radius * 2f)
        {
            return; // the spheres already touch
        }
        axis /= length;
        var helper = MathF.Abs(axis.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(axis, helper)) * radius;
        var v = Vector3.Cross(axis, Vector3.Normalize(u)) * radius;
        var tip = to - (axis * radius);
        Span<Vector3> corners = [from + u, from + v, from - u, from - v];
        for (var i = 0; i < 4; i++)
        {
            lines.Add(new ColoredLine(corners[i], tip, color));
            lines.Add(new ColoredLine(corners[i], corners[(i + 1) % 4], color));
        }
    }
}
