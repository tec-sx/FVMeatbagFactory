using System.Numerics;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Components;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.Scene;

/// <summary>A bone under the cursor.</summary>
public readonly record struct BonePick(SkeletonComponent Skeleton, int Bone, float DistancePixels);

/// <summary>
/// Finds the bone under a viewport point. Clicking a joint sphere picks that joint; clicking a
/// bone picks the joint it starts from, as in Maya.
/// </summary>
public static class BonePicker
{
    /// <summary>Extra slack around joints and bones, in pixels, so thin lines are easy to hit.</summary>
    public const float TolerancePixels = 6f;

    /// <param name="viewProjection">View × projection in System.Numerics convention (clip +Y up).</param>
    /// <param name="point">Cursor position in pixels, origin top left.</param>
    public static Maybe<BonePick> Pick(SceneRoot scene, Matrix4x4 viewProjection, Vector2 viewportSize, Vector2 point)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var best = Maybe<BonePick>.None;
        foreach (var skeleton in scene.Skeletons)
        {
            if (skeleton.DisplayNode is not { } node)
            {
                continue;
            }
            var output = node.OutputValues;
            if (Pick(skeleton, output, viewProjection, viewportSize, point).TryGetValue(out var pick)
                && (best.IsNone || pick.DistancePixels < best.Value.DistancePixels))
            {
                best = pick;
            }
        }
        return best;
    }

    private static Maybe<BonePick> Pick(SkeletonComponent skeleton, BoneDisplayOutput display, Matrix4x4 viewProjection, Vector2 viewportSize, Vector2 point)
    {
        var joints = display.Joints.Span;
        if (joints.IsEmpty)
        {
            return Maybe<BonePick>.None;
        }

        var screen = new Vector2?[joints.Length];
        var radii = new float[joints.Length];
        for (var i = 0; i < joints.Length; i++)
        {
            screen[i] = ToScreen(joints[i], viewProjection, viewportSize);
            // Project a point one radius away along the camera-independent X axis to estimate the on-screen radius.
            var edge = ToScreen(joints[i] + (Vector3.UnitX * display.JointRadius), viewProjection, viewportSize);
            radii[i] = screen[i] is { } c && edge is { } e ? Vector2.Distance(c, e) : 0f;
        }

        var bestBone = -1;
        var bestDistance = float.MaxValue;
        // Joints first: a click inside a sphere always wins over a bone passing nearby.
        for (var i = 0; i < joints.Length; i++)
        {
            if (screen[i] is not { } c)
            {
                continue;
            }
            var distance = Vector2.Distance(c, point);
            if (distance <= radii[i] + TolerancePixels && distance < bestDistance)
            {
                bestBone = i;
                bestDistance = distance;
            }
        }
        if (bestBone < 0)
        {
            for (var bone = 0; bone < joints.Length; bone++)
            {
                foreach (var child in skeleton.Skeleton.ChildrenOf(bone))
                {
                    if (screen[bone] is { } a && screen[child] is { } b)
                    {
                        var distance = DistanceToSegment(point, a, b);
                        if (distance <= TolerancePixels && distance < bestDistance)
                        {
                            bestBone = bone;
                            bestDistance = distance;
                        }
                    }
                }
            }
        }
        return bestBone < 0 ? Maybe<BonePick>.None : new BonePick(skeleton, bestBone, bestDistance);
    }

    /// <summary>Projects a world point to pixels (origin top left), or null when it is behind the camera.</summary>
    public static Vector2? ToScreen(Vector3 world, Matrix4x4 viewProjection, Vector2 viewportSize)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        if (clip.W <= 1e-6f)
        {
            return null;
        }
        var ndc = new Vector2(clip.X, clip.Y) / clip.W;
        return new Vector2((ndc.X * 0.5f + 0.5f) * viewportSize.X, (0.5f - (ndc.Y * 0.5f)) * viewportSize.Y);
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        var t = lengthSquared <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(p, a + (ab * t));
    }
}
