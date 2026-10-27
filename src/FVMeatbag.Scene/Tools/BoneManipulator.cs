using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Components;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.Scene.Tools;

/// <summary>The viewport tools, as in Maya's toolbox (keys Q, W, E, R).</summary>
public enum TransformTool
{
    Select,
    Move,
    Rotate,
    Scale,
}

/// <summary>A grabbable part of the transform gizmo.</summary>
public enum GizmoHandle
{
    None,
    X,
    Y,
    Z,

    /// <summary>Move: in the view plane. Rotate: about the view axis (outer ring). Scale: uniformly.</summary>
    Center,
}

/// <summary>
/// The move, rotate and scale gizmos for the selected bone, Maya style: move along world axes,
/// rotate and scale along the bone's own axes. It draws the gizmo as overlay lines, finds the
/// handle under the pointer and turns pointer drags into pose edits.
/// </summary>
/// <remarks>
/// Every drag edits the pose relative to the bone's transform when the drag started, so a drag
/// never accumulates rounding errors and returning the pointer to where it started restores the bone.
/// </remarks>
public sealed class BoneManipulator(SceneRoot scene)
{
    /// <summary>Gizmo size on screen, in pixels.</summary>
    public const float SizePixels = 100f;

    /// <summary>How close the pointer must be to a handle, in pixels.</summary>
    public const float HitPixels = 8f;

    private const int RingSegments = 64;

    public static readonly Vector3 XColor = new(0.95f, 0.25f, 0.25f);
    public static readonly Vector3 YColor = new(0.35f, 0.95f, 0.35f);
    public static readonly Vector3 ZColor = new(0.30f, 0.50f, 1.00f);
    public static readonly Vector3 CenterColor = new(0.40f, 0.95f, 0.95f);

    /// <summary>Maya draws the handle under the pointer, and the one being dragged, in yellow.</summary>
    public static readonly Vector3 ActiveColor = new(1.00f, 0.95f, 0.20f);

    private TransformTool _tool = TransformTool.Select;
    private DragState? _drag;

    /// <summary>Raised when the tool, the hovered handle or the dragged handle changes.</summary>
    public event EventHandler? Changed;

    public TransformTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value)
            {
                return;
            }
            EndDrag();
            _tool = value;
            Hovered = GizmoHandle.None;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The handle under the pointer.</summary>
    public GizmoHandle Hovered { get; private set; }

    public bool IsDragging => _drag is not null;

    /// <summary>The handle being dragged, or <see cref="GizmoHandle.None"/>.</summary>
    public GizmoHandle Active => _drag?.Handle ?? GizmoHandle.None;

    /// <summary>The selected bone the gizmo works on, if any.</summary>
    public Maybe<(SkeletonComponent Skeleton, int Bone)> Target =>
        scene.Skeletons.Select(s => s.SelectedBone.Map(b => (s, b))).FirstOrDefault(t => t.HasValue);

    /// <summary>The gizmo's overlay lines; empty for the select tool or when no bone is selected.</summary>
    public IReadOnlyList<ColoredLine> Lines(ViewportCamera camera) =>
        [.. Build(camera).Select(p => new ColoredLine(p.Line.From, p.Line.To, IsActive(p.Handle) ? ActiveColor : p.Line.Color))];

    /// <summary>The handle within <see cref="HitPixels"/> of <paramref name="point"/>.</summary>
    public GizmoHandle HitTest(ViewportCamera camera, Vector2 point)
    {
        var best = GizmoHandle.None;
        var bestDistance = HitPixels;
        foreach (var (handle, line, front) in Build(camera))
        {
            if (!front || camera.ToScreen(line.From) is not { } a || camera.ToScreen(line.To) is not { } b)
            {
                continue;
            }
            var distance = DistanceToSegment(point, a, b);
            // The center handle sits on top of the axes' starts, so it wins ties there.
            if (distance < bestDistance || (handle == GizmoHandle.Center && distance <= bestDistance && _tool != TransformTool.Rotate))
            {
                best = handle;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>Updates <see cref="Hovered"/>. Returns true if it changed, so the viewport can redraw.</summary>
    public bool UpdateHover(ViewportCamera camera, Vector2 point)
    {
        var hovered = _drag is null ? HitTest(camera, point) : Hovered;
        if (hovered == Hovered)
        {
            return false;
        }
        Hovered = hovered;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Starts dragging the handle under <paramref name="point"/>. Returns false when there is none.</summary>
    public bool BeginDrag(ViewportCamera camera, Vector2 point)
    {
        if (!Frame(camera).TryGetValue(out var frame))
        {
            return false;
        }
        var handle = HitTest(camera, point);
        if (handle == GizmoHandle.None)
        {
            return false;
        }

        var drag = new DragState(frame, handle, point, frame.Skeleton.Pose[frame.Bone]);
        var axis = AxisOf(frame, handle, camera);
        switch (_tool)
        {
            case TransformTool.Move when handle == GizmoHandle.Center:
                drag.PlaneStart = IntersectPlane(camera.Ray(point), frame.Center, camera.Forward);
                break;
            case TransformTool.Move or TransformTool.Scale when handle != GizmoHandle.Center:
                drag.AxisStart = AxisParameter(camera.Ray(point), frame.Center, axis);
                break;
            case TransformTool.Rotate:
                (drag.ScreenTangent, drag.PixelsPerRadian) = RingTangent(camera, frame, handle, point);
                break;
        }
        _drag = drag;
        Hovered = handle;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Applies the drag so far to the bone.</summary>
    public void Drag(ViewportCamera camera, Vector2 point)
    {
        if (_drag is not { } drag)
        {
            return;
        }
        var frame = drag.Frame;
        var skeleton = frame.Skeleton;
        var axis = AxisOf(frame, drag.Handle, camera);
        switch (_tool)
        {
            case TransformTool.Move when drag.Handle == GizmoHandle.Center:
                if (drag.PlaneStart is { } start && IntersectPlane(camera.Ray(point), frame.Center, camera.Forward) is { } now)
                {
                    skeleton.MoveBone(frame.Bone, drag.Start, now - start);
                }
                break;
            case TransformTool.Move:
                if (drag.AxisStart is { } from && AxisParameter(camera.Ray(point), frame.Center, axis) is { } to)
                {
                    skeleton.MoveBone(frame.Bone, drag.Start, axis * (to - from));
                }
                break;
            case TransformTool.Rotate:
                var radians = Vector2.Dot(point - drag.Point, drag.ScreenTangent) / drag.PixelsPerRadian;
                skeleton.RotateBone(frame.Bone, drag.Start, axis, radians);
                break;
            case TransformTool.Scale when drag.Handle == GizmoHandle.Center:
                var delta = point - drag.Point;
                skeleton.ScaleBone(frame.Bone, drag.Start, new Vector3(MathF.Exp((delta.X - delta.Y) * 0.01f)));
                break;
            case TransformTool.Scale:
                if (drag.AxisStart is { } s0 && AxisParameter(camera.Ray(point), frame.Center, axis) is { } s1)
                {
                    var factor = MathF.Max(1f + ((s1 - s0) / frame.Length(camera)), 0.01f);
                    var factors = Vector3.One;
                    factors[(int)drag.Handle - (int)GizmoHandle.X] = factor;
                    skeleton.ScaleBone(frame.Bone, drag.Start, factors);
                }
                break;
        }
    }

    public void EndDrag()
    {
        if (_drag is null)
        {
            return;
        }
        _drag = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool IsActive(GizmoHandle handle) => handle != GizmoHandle.None && (_drag is not null ? _drag.Handle == handle : Hovered == handle);

    /// <summary>Where the gizmo sits and how it is oriented. During a drag it stays where the drag started.</summary>
    private Maybe<GizmoFrame> Frame(ViewportCamera camera)
    {
        if (_drag is { } drag)
        {
            return drag.Frame with { Center = drag.Frame.Skeleton.BoneWorldMatrix(drag.Frame.Bone).Translation };
        }
        if (_tool == TransformTool.Select || !Target.TryGetValue(out var target))
        {
            return Maybe<GizmoFrame>.None;
        }
        var world = target.Skeleton.BoneWorldMatrix(target.Bone);
        Vector3[] axes = _tool == TransformTool.Move
            ? [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ]
            : [Row(world, 1), Row(world, 2), Row(world, 3)];
        return new GizmoFrame(target.Skeleton, target.Bone, world.Translation, axes);
    }

    /// <summary>World axis a handle works along or around.</summary>
    private static Vector3 AxisOf(GizmoFrame frame, GizmoHandle handle, ViewportCamera camera) => handle switch
    {
        GizmoHandle.X => frame.Axes[0],
        GizmoHandle.Y => frame.Axes[1],
        GizmoHandle.Z => frame.Axes[2],
        _ => -camera.Forward,
    };

    /// <summary>Gizmo lines tagged with their handle, and whether they face the camera (rings hide their back half).</summary>
    private List<(GizmoHandle Handle, ColoredLine Line, bool Front)> Build(ViewportCamera camera)
    {
        var parts = new List<(GizmoHandle, ColoredLine, bool)>();
        if (!Frame(camera).TryGetValue(out var frame))
        {
            return parts;
        }
        var c = frame.Center;
        var length = frame.Length(camera);
        var right = Vector3.Normalize(Vector3.Cross(camera.Forward, Vector3.UnitY));
        if (!float.IsFinite(right.X))
        {
            right = Vector3.UnitX;
        }
        var up = Vector3.Cross(right, camera.Forward);
        Vector3[] colors = [XColor, YColor, ZColor];
        for (var k = 0; k < 3; k++)
        {
            var handle = GizmoHandle.X + k;
            var axis = frame.Axes[k];
            if (_tool != TransformTool.Rotate && MathF.Abs(Vector3.Dot(axis, camera.Forward)) > 0.97f)
            {
                continue; // pointing at the camera: too short on screen to grab, as in Maya
            }
            switch (_tool)
            {
                case TransformTool.Move:
                    Add(parts, handle, c + (axis * length * 0.18f), c + (axis * length * 0.82f), colors[k]);
                    AddCone(parts, handle, c + (axis * length * 0.82f), c + (axis * length), length * 0.06f, colors[k]);
                    break;
                case TransformTool.Scale:
                    Add(parts, handle, c + (axis * length * 0.18f), c + (axis * length * 0.9f), colors[k]);
                    AddCube(parts, handle, c + (axis * length * 0.95f), frame.Axes, length * 0.05f, colors[k]);
                    break;
                case TransformTool.Rotate:
                    var (u, v) = Perpendiculars(axis);
                    AddRing(parts, handle, c, u * length * 0.8f, v * length * 0.8f, colors[k], camera);
                    break;
            }
        }
        switch (_tool)
        {
            case TransformTool.Move:
                AddSquare(parts, GizmoHandle.Center, c, right * length * 0.09f, up * length * 0.09f, CenterColor);
                break;
            case TransformTool.Scale:
                AddCube(parts, GizmoHandle.Center, c, frame.Axes, length * 0.07f, CenterColor);
                break;
            case TransformTool.Rotate:
                // Faces the camera, so all of it is in front.
                AddRing(parts, GizmoHandle.Center, c, right * length, up * length, CenterColor, null);
                break;
        }
        return parts;
    }

    /// <summary>The screen direction a ring moves in where it was grabbed, and how many pixels make one radian.</summary>
    private static (Vector2 Tangent, float PixelsPerRadian) RingTangent(ViewportCamera camera, GizmoFrame frame, GizmoHandle handle, Vector2 point)
    {
        var axis = AxisOf(frame, handle, camera);
        var radius = frame.Length(camera) * (handle == GizmoHandle.Center ? 1f : 0.8f);
        var (u, v) = Perpendiculars(axis);
        // The ring point nearest the pointer on screen.
        var grab = frame.Center + (u * radius);
        var nearest = float.MaxValue;
        for (var i = 0; i < RingSegments; i++)
        {
            var angle = 2 * MathF.PI * i / RingSegments;
            var p = frame.Center + (((u * MathF.Cos(angle)) + (v * MathF.Sin(angle))) * radius);
            if (camera.ToScreen(p) is { } s && Vector2.Distance(s, point) < nearest)
            {
                nearest = Vector2.Distance(s, point);
                grab = p;
            }
        }
        // A positive turn about the axis moves the grab point along axis × radius.
        var tangent = Vector3.Normalize(Vector3.Cross(axis, grab - frame.Center));
        const float Step = 0.05f;
        if (camera.ToScreen(grab) is { } s0 && camera.ToScreen(grab + (tangent * radius * Step)) is { } s1)
        {
            var screen = s1 - s0;
            var pixels = screen.Length() / Step;
            if (pixels > 20f)
            {
                return (Vector2.Normalize(screen), pixels);
            }
        }
        // Seen edge-on the ring barely moves on screen; fall back to horizontal dragging.
        return (Vector2.UnitX, SizePixels);
    }

    /// <summary>Parameter along the axis line through <paramref name="center"/> of the point closest to the ray, or null when they are nearly parallel.</summary>
    private static float? AxisParameter((Vector3 Origin, Vector3 Direction) ray, Vector3 center, Vector3 axis)
    {
        var w = center - ray.Origin;
        var b = Vector3.Dot(axis, ray.Direction);
        var c = Vector3.Dot(ray.Direction, ray.Direction);
        var a = Vector3.Dot(axis, axis);
        var denominator = (a * c) - (b * b);
        if (MathF.Abs(denominator) < 1e-4f)
        {
            return null;
        }
        var d = Vector3.Dot(axis, w);
        var e = Vector3.Dot(ray.Direction, w);
        return ((b * e) - (c * d)) / denominator;
    }

    private static Vector3? IntersectPlane((Vector3 Origin, Vector3 Direction) ray, Vector3 point, Vector3 normal)
    {
        var denominator = Vector3.Dot(ray.Direction, normal);
        if (MathF.Abs(denominator) < 1e-6f)
        {
            return null;
        }
        var t = Vector3.Dot(point - ray.Origin, normal) / denominator;
        return ray.Origin + (ray.Direction * t);
    }

    private static Vector3 Row(Matrix4x4 m, int row)
    {
        var v = row switch
        {
            1 => new Vector3(m.M11, m.M12, m.M13),
            2 => new Vector3(m.M21, m.M22, m.M23),
            _ => new Vector3(m.M31, m.M32, m.M33),
        };
        return v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : (row == 1 ? Vector3.UnitX : row == 2 ? Vector3.UnitY : Vector3.UnitZ);
    }

    private static (Vector3 U, Vector3 V) Perpendiculars(Vector3 axis)
    {
        var helper = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(helper, axis));
        return (u, Vector3.Cross(axis, u));
    }

    private static void Add(List<(GizmoHandle, ColoredLine, bool)> parts, GizmoHandle handle, Vector3 from, Vector3 to, Vector3 color) =>
        parts.Add((handle, new ColoredLine(from, to, color), true));

    private static void AddCone(List<(GizmoHandle, ColoredLine, bool)> parts, GizmoHandle handle, Vector3 baseCenter, Vector3 tip, float radius, Vector3 color)
    {
        var (u, v) = Perpendiculars(Vector3.Normalize(tip - baseCenter));
        const int Sides = 10;
        var previous = baseCenter + (u * radius);
        for (var i = 1; i <= Sides; i++)
        {
            var angle = 2 * MathF.PI * i / Sides;
            var point = baseCenter + (((u * MathF.Cos(angle)) + (v * MathF.Sin(angle))) * radius);
            Add(parts, handle, previous, point, color);
            Add(parts, handle, point, tip, color);
            previous = point;
        }
        Add(parts, handle, baseCenter, tip, color);
    }

    private static void AddCube(List<(GizmoHandle, ColoredLine, bool)> parts, GizmoHandle handle, Vector3 center, Vector3[] axes, float half, Vector3 color)
    {
        Vector3 Corner(int i) => center
            + (axes[0] * (((i & 1) == 0) ? -half : half))
            + (axes[1] * (((i & 2) == 0) ? -half : half))
            + (axes[2] * (((i & 4) == 0) ? -half : half));
        for (var i = 0; i < 8; i++)
        {
            for (var bit = 1; bit < 8; bit <<= 1)
            {
                if ((i & bit) == 0)
                {
                    Add(parts, handle, Corner(i), Corner(i | bit), color);
                }
            }
        }
    }

    private static void AddSquare(List<(GizmoHandle, ColoredLine, bool)> parts, GizmoHandle handle, Vector3 center, Vector3 u, Vector3 v, Vector3 color)
    {
        Vector3[] corners = [center - u - v, center + u - v, center + u + v, center - u + v];
        for (var i = 0; i < 4; i++)
        {
            Add(parts, handle, corners[i], corners[(i + 1) % 4], color);
        }
    }

    /// <summary>A circle; with a camera, the half facing away from it is dimmed and not grabbable.</summary>
    private static void AddRing(List<(GizmoHandle, ColoredLine, bool)> parts, GizmoHandle handle, Vector3 center, Vector3 u, Vector3 v, Vector3 color, ViewportCamera? camera)
    {
        var previous = center + u;
        for (var i = 1; i <= RingSegments; i++)
        {
            var angle = 2 * MathF.PI * (i - 0.5f) / RingSegments;
            var middle = center + (u * MathF.Cos(angle)) + (v * MathF.Sin(angle));
            var point = center + (u * MathF.Cos(2 * MathF.PI * i / RingSegments)) + (v * MathF.Sin(2 * MathF.PI * i / RingSegments));
            // A ring facing the camera is all front; a little slack keeps rounding from dimming parts of it.
            var front = camera is not { } cam
                || Vector3.Dot(Vector3.Normalize(middle - center), Vector3.Normalize(cam.Eye - center)) > -0.1f;
            parts.Add((handle, new ColoredLine(previous, point, front ? color : color * 0.35f), front));
            previous = point;
        }
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        var t = lengthSquared <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(p, a + (ab * t));
    }

    private readonly record struct GizmoFrame(SkeletonComponent Skeleton, int Bone, Vector3 Center, Vector3[] Axes)
    {
        /// <summary>World length of an axis handle, so the gizmo keeps its size on screen.</summary>
        public float Length(ViewportCamera camera) => SizePixels * camera.WorldPerPixel(Center);
    }

    private sealed class DragState(GizmoFrame frame, GizmoHandle handle, Vector2 point, BoneTransform start)
    {
        public GizmoFrame Frame { get; } = frame;
        public GizmoHandle Handle { get; } = handle;
        public Vector2 Point { get; } = point;

        /// <summary>The bone's transform when the drag started.</summary>
        public BoneTransform Start { get; } = start;

        public float? AxisStart { get; set; }
        public Vector3? PlaneStart { get; set; }
        public Vector2 ScreenTangent { get; set; }
        public float PixelsPerRadian { get; set; } = SizePixels;
    }
}
