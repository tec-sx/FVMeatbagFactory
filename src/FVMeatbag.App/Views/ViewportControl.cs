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
/// Mouse: a left click selects the bone under the cursor (or clears the bone selection), left or
/// middle drag orbits, right drag or Shift+drag pans, the wheel zooms, F frames the scene.
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
    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x24));

    private WriteableBitmap? _bitmap;
    private bool _dirty;
    private string? _error;
    private Point _lastPointer;
    private Point _pressPointer;
    private bool _dragging;
    private bool _clickCandidate;

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
        context.FillRectangle(Background, bounds);
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
            context.DrawImage(_bitmap, bounds);
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
            }
            if (change.NewValue is AppHost newHost)
            {
                newHost.GraphEvaluated += OnGraphEvaluated;
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
        _clickCandidate = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
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
        if (!_dragging || Host?.Scene.Camera is not { } camera)
        {
            return;
        }
        var position = e.GetPosition(this);
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

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private void OnGraphEvaluated(object? sender, int evaluated) => RequestFrame();

    /// <summary>Selects the bone under <paramref name="point"/>, or clears the bone selection when there is none.</summary>
    private void PickBone(Point point)
    {
        if (Host?.Scene is not { } scene || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }
        var camera = scene.Camera.CameraNode!.OutputValues;
        var size = new System.Numerics.Vector2((float)Bounds.Width, (float)Bounds.Height);
        var pick = BonePicker.Pick(scene, camera.View * camera.Projection, size, new System.Numerics.Vector2((float)point.X, (float)point.Y));
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
            renderer.RenderFrame(BuildFrame(host.Scene, (uint)width, (uint)height), BuildDraws(host.Scene), BuildLines(host.Scene));

            if (_bitmap is null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height)
            {
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96 * scaling, 96 * scaling), PixelFormat.Bgra8888, AlphaFormat.Opaque);
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

    private static List<LineSegment> BuildLines(SceneRoot scene)
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
        return lines;
    }

    private static List<MeshDraw> BuildDraws(SceneRoot scene)
    {
        var draws = new List<MeshDraw>();
        foreach (var mesh in scene.Meshes)
        {
            if (scene.FindAsset(mesh).TryGetValue(out var asset) && asset.Geometry.TryGetValue(out var geometry))
            {
                draws.Add(new MeshDraw(new RenderMesh(asset.Id, asset.Version, geometry.Positions, geometry.Normals, geometry.Indices), mesh.WorldMatrix));
            }
        }
        return draws;
    }
}
