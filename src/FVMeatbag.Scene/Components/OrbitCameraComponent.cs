using System.Numerics;
using FVMeatbag.Core.Project;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.Scene.Components;

/// <summary>
/// The scene's one camera. It orbits and pans around a target point. Created by
/// <see cref="SceneRoot"/>; it can be adjusted but never added or removed.
/// </summary>
public sealed class OrbitCameraComponent : SceneComponent
{
    private OrbitCameraSettings _settings = OrbitCameraSettings.Default;
    private float _aspectRatio = 1f;

    internal OrbitCameraComponent() : base("Camera")
    {
    }

    public override bool IsRemovable => false;

    /// <summary>Computes view and projection; exists while the camera is attached.</summary>
    public Node<OrbitCameraInput, OrbitCameraOutput>? CameraNode { get; private set; }

    public OrbitCameraSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value.Normalized();
            CameraNode?.UpdateInputs((ref OrbitCameraInput i) => i.Settings = _settings);
        }
    }

    /// <summary>Viewport width / height.</summary>
    public float AspectRatio
    {
        get => _aspectRatio;
        set
        {
            _aspectRatio = value > 0f ? value : 1f;
            CameraNode?.UpdateInputs((ref OrbitCameraInput i) => i.AspectRatio = _aspectRatio);
        }
    }

    /// <summary>Rotates around the target.</summary>
    public void Orbit(float deltaYawDegrees, float deltaPitchDegrees) =>
        Settings = _settings with
        {
            YawDegrees = _settings.YawDegrees + deltaYawDegrees,
            PitchDegrees = _settings.PitchDegrees + deltaPitchDegrees,
        };

    /// <summary>
    /// Slides the target parallel to the view plane so the scene follows the cursor. Deltas are in
    /// pixels (y down) of a viewport <paramref name="viewportHeight"/> pixels high.
    /// </summary>
    public void Pan(float deltaX, float deltaY, float viewportHeight)
    {
        if (viewportHeight <= 0f)
        {
            return;
        }
        var forward = Vector3.Normalize(_settings.Target - _settings.Eye);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        var worldPerPixel = 2f * _settings.Distance * MathF.Tan(float.DegreesToRadians(_settings.FieldOfViewDegrees) * 0.5f) / viewportHeight;
        Settings = _settings with { Target = _settings.Target + ((-right * deltaX) + (up * deltaY)) * worldPerPixel };
    }

    /// <summary>Moves towards (factor &lt; 1) or away from (factor &gt; 1) the target.</summary>
    public void Zoom(float factor) => Settings = _settings with { Distance = _settings.Distance * factor };

    /// <summary>Aims at <paramref name="center"/> from far enough away to see a sphere of <paramref name="radius"/>.</summary>
    public void Frame(Vector3 center, float radius)
    {
        var halfFov = float.DegreesToRadians(_settings.FieldOfViewDegrees) * 0.5f;
        var distance = MathF.Max(radius, 0.01f) / MathF.Sin(halfFov) * 1.1f;
        Settings = _settings with { Target = center, Distance = distance };
    }

    protected override void OnAttached(Graph.Graph graph) =>
        CameraNode = graph.AddNode(
            new OrbitCameraOperation(),
            $"{Name}.View",
            new OrbitCameraInput { Settings = _settings, AspectRatio = _aspectRatio });

    protected override void OnDetaching() => CameraNode = null;
}
