using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FVMeatbag.Graph;
using FVMeatbag.Rendering;
using FVMeatbag.Scene;
using FVMeatbag.Scene.Components;
using FVMeatbag.Scene.Tools;
using LineSegment = FVMeatbag.Rendering.LineSegment;

namespace FVMeatbag.App.Views;

/// <summary>
/// Shows the scene rendered by the Vulkan backend and drives the orbit camera.
/// </summary>
/// <remarks>
/// The backend renders offscreen; each frame is copied into a <see cref="WriteableBitmap"/>
/// (presentation step 1 in docs/ARCHITECTURE.md). A frame is only rendered when the graph changed
/// or the control was resized.
/// <para>
/// Mouse: a left click selects the bone under the cursor (or clears the bone selection), left
/// drag on a gizmo handle moves, rotates or scales the selected bone, left or middle drag
/// elsewhere orbits, right drag or Shift+drag pans, the wheel zooms, F frames the scene.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "The bitmap is released when the control leaves the visual tree, which is the lifetime Avalonia gives controls.")]
public sealed class ViewportControl : Control
{
    public static readonly StyledProperty<AppHost?> HostProperty =
        AvaloniaProperty.Register<ViewportControl, AppHost?>(nameof(Host));

    public static readonly StyledProperty<MeshDisplayMode> DisplayModeProperty =
        AvaloniaProperty.Register<ViewportControl, MeshDisplayMode>(nameof(DisplayMode), MeshDisplayMode.Solid);

    private const float OrbitDegreesPerPixel = 0.4f;

    /// <summary>A press and release closer than this, in pixels, is a click rather than a drag.</summary>
    private const double ClickSlop = 3;
    private static readonly IBrush FallbackBackground = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x29));

    private WriteableBitmap? _bitmap;
    private bool _dirty;
    private string? _error;
    private Point _lastPointer;
    private Point _pressPointer;
    private bool _dragging;
    private bool _clickCandidate;
    private bool _gizmoDragging;
    private TopLevel? _topLevel;

    public ViewportControl()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public AppHost? Host
    {
        get => GetValue(HostProperty);
        set => SetValue(HostProperty, value);
    }

    public MeshDisplayMode DisplayMode
    {
        get => GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var bounds = new Rect(Bounds.Size);
        var background = this.TryFindResource("Theme.Viewport", ActualThemeVariant, out var resource) && resource is IBrush brush
            ? brush
            : FallbackBackground;
        context.FillRectangle(background, bounds);
        if (_error is not null)
        {
            var text = new FormattedText(_error, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.Gray)
            {
                MaxTextWidth = Math.Max(1, bounds.Width - 32),
            };
            context.DrawText(text, new Point(16, 16));
        }
        else if (_bitmap is not null)
        {
            context.DrawImage(_bitmap, new Rect(_bitmap.Size), bounds);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == HostProperty)
        {
            if (change.OldValue is AppHost oldHost)
            {
                oldHost.GraphEvaluated -= OnGraphEvaluated;
                oldHost.Scene.Manipulator.Changed -= OnManipulatorChanged;
            }
            if (change.NewValue is AppHost newHost)
            {
                newHost.GraphEvaluated += OnGraphEvaluated;
                newHost.Scene.Manipulator.Changed += OnManipulatorChanged;
                UpdateAspectRatio();
            }
            RequestFrame();
        }
        else if (change.Property == BoundsProperty)
        {
            UpdateAspectRatio();
            RequestFrame();
        }
        else if (change.Property == DisplayModeProperty)
        {
            RequestFrame();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerPressed(e);
        Focus();
        _lastPointer = e.GetPosition(this);
        _pressPointer = _lastPointer;
        var left = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        if (left && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && Host?.Scene is { } scene && ToolCamera(scene) is { } camera
            && scene.Manipulator.BeginDrag(camera, ToVector(_lastPointer)))
        {
            _gizmoDragging = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        _clickCandidate = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        if (_gizmoDragging)
        {
            _gizmoDragging = false;
            Host?.Scene.Manipulator.EndDrag();
            return;
        }
        _dragging = false;
        if (_clickCandidate && e.InitialPressMouseButton == MouseButton.Left)
        {
            PickBone(e.GetPosition(this));
        }
        _clickCandidate = false;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerMoved(e);
        if (Host?.Scene is not { } scene)
        {
            return;
        }
        var position = e.GetPosition(this);
        if (_gizmoDragging)
        {
            if (ToolCamera(scene) is { } toolCamera)
            {
                scene.Manipulator.Drag(toolCamera, ToVector(position));
            }
            e.Handled = true;
            return;
        }
        if (!_dragging)
        {
            if (ToolCamera(scene) is { } hoverCamera)
            {
                scene.Manipulator.UpdateHover(hoverCamera, ToVector(position));
            }
            return;
        }
        var camera = scene.Camera;
        var delta = position - _lastPointer;
        _lastPointer = position;
        var moved = position - _pressPointer;
        if (_clickCandidate && Math.Abs(moved.X) + Math.Abs(moved.Y) <= ClickSlop)
        {
            return; // may still be a click; start orbiting once the pointer really moves
        }
        _clickCandidate = false;

        var properties = e.GetCurrentPoint(this).Properties;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (properties.IsRightButtonPressed || (shift && (properties.IsLeftButtonPressed || properties.IsMiddleButtonPressed)))
        {
            camera.Pan((float)delta.X, (float)delta.Y, (float)Bounds.Height);
        }
        else if (properties.IsLeftButtonPressed || properties.IsMiddleButtonPressed)
        {
            // Like grabbing the model: drag right turns its front to the right, drag down tips its top towards you.
            camera.Orbit(-(float)delta.X * OrbitDegreesPerPixel, (float)delta.Y * OrbitDegreesPerPixel);
        }
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_gizmoDragging)
        {
            _gizmoDragging = false;
            Host?.Scene.Manipulator.EndDrag();
        }
        _dragging = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerWheelChanged(e);
        Host?.Scene.Camera.Zoom(MathF.Pow(0.9f, (float)e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.Key == Key.F && Host is { } host)
        {
            host.Scene.FrameAll();
            e.Handled = true;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.ScalingChanged += OnScalingChanged;
        }
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
        {
            _topLevel.ScalingChanged -= OnScalingChanged;
            _topLevel = null;
        }
        base.OnDetachedFromVisualTree(e);
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private void OnScalingChanged(object? sender, EventArgs e) => RequestFrame();

    private void OnGraphEvaluated(object? sender, int evaluated) => RequestFrame();

    private void OnManipulatorChanged(object? sender, EventArgs e) => RequestFrame();

    private static System.Numerics.Vector2 ToVector(Point point) => new((float)point.X, (float)point.Y);

    /// <summary>The camera as tools see it, in the control's own units (the same as pointer positions).</summary>
    private ViewportCamera? ToolCamera(SceneRoot scene)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || scene.Camera.CameraNode is not { } node)
        {
            return null;
        }
        var camera = node.OutputValues;
        return new ViewportCamera(camera.View, camera.Projection, camera.Eye, new System.Numerics.Vector2((float)Bounds.Width, (float)Bounds.Height));
    }

    /// <summary>Selects the bone under <paramref name="point"/>, or clears the bone selection when there is none.</summary>
    private void PickBone(Point point)
    {
        if (Host?.Scene is not { } scene || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }
        var camera = scene.Camera.CameraNode!.OutputValues;
        var size = new System.Numerics.Vector2((float)Bounds.Width, (float)Bounds.Height);
        var pick = BonePicker.Pick(scene, camera.View * camera.Projection, size, ToVector(point));
        foreach (var skeleton in scene.Skeletons)
        {
            if (!pick.TryGetValue(out var hit) || hit.Skeleton != skeleton)
            {
                skeleton.SelectedBone = Maybe<int>.None;
            }
        }
        if (pick.TryGetValue(out var picked))
        {
            picked.Skeleton.SelectedBone = Maybe<int>.Some(picked.Bone);
        }
    }

    private void UpdateAspectRatio()
    {
        if (Host is { } host && Bounds.Width > 0 && Bounds.Height > 0)
        {
            host.Scene.Camera.AspectRatio = (float)(Bounds.Width / Bounds.Height);
        }
    }

    private void RequestFrame()
    {
        if (_dirty)
        {
            return;
        }
        _dirty = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(RenderScene, Avalonia.Threading.DispatcherPriority.Render);
    }

    private void RenderScene()
    {
        _dirty = false;
        if (Host is not { } host || _error is not null)
        {
            return;
        }
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = (int)Math.Round(Bounds.Width * scaling);
        var height = (int)Math.Round(Bounds.Height * scaling);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        try
        {
            var renderer = host.Renderer;
            if (!renderer.IsInitialized)
            {
                renderer.Initialize();
            }
            renderer.RenderFrame(BuildFrame(host.Scene, (uint)width, (uint)height), BuildDraws(host.Scene), BuildLines(host.Scene, ToolCamera(host.Scene)));

            if (_bitmap is null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height)
            {
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            }
            using (var framebuffer = _bitmap.Lock())
            {
                renderer.CopyPixels(framebuffer.Address, framebuffer.RowBytes);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            _error = $"The Vulkan viewport could not start.\n{e.Message}\n\nUpdate the GPU driver, or check that Vulkan is installed.";
        }
        InvalidateVisual();
    }

    private FrameContext BuildFrame(SceneRoot scene, uint width, uint height)
    {
        var camera = scene.Camera.CameraNode!.OutputValues;
        var light = scene.Light.LightNode!.OutputValues;
        return new FrameContext(width, height, camera.View, camera.Projection, light.DirectionToLight, light.Radiance, 0, DisplayMode);
    }

    private static List<LineSegment> BuildLines(SceneRoot scene, ViewportCamera? camera)
    {
        var lines = new List<LineSegment>();
        foreach (var skeleton in scene.Skeletons)
        {
            if (skeleton.DisplayNode is not { } node)
            {
                continue;
            }
            foreach (var line in node.OutputValues.Lines.Span)
            {
                lines.Add(new LineSegment(line.From, line.To, line.Color));
            }
        }
        // The gizmo last, so it is drawn over the bones.
        if (camera is { } toolCamera)
        {
            foreach (var line in scene.Manipulator.Lines(toolCamera))
            {
                lines.Add(new LineSegment(line.From, line.To, line.Color));
            }
        }
        return lines;
    }

    private static List<MeshDraw> BuildDraws(SceneRoot scene)
    {
        var draws = new List<MeshDraw>();
        foreach (var mesh in scene.Meshes)
        {
            if (!scene.FindAsset(mesh).TryGetValue(out var asset) || !asset.Geometry.TryGetValue(out var geometry))
            {
                continue;
            }
            // Rigged meshes are drawn as posed by their skeleton's skinning node.
            var skinned = mesh.Children.OfType<SkeletonComponent>().FirstOrDefault()?.SkinningNode?.OutputValues;
            var render = skinned is { Positions.IsEmpty: false } posed
                ? new RenderMesh(asset.Id, posed.Version, posed.Positions, posed.Normals, geometry.Indices)
                : new RenderMesh(asset.Id, asset.Version, geometry.Positions, geometry.Normals, geometry.Indices);
            draws.Add(new MeshDraw(render, mesh.WorldMatrix));
        }
        return draws;
    }
}
