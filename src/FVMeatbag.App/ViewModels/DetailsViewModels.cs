using CommunityToolkit.Mvvm.Input;
using FVMeatbag.Core.Project;
using FVMeatbag.Scene;
using FVMeatbag.Scene.Components;

namespace FVMeatbag.App.ViewModels;

/// <summary>Details panel content for the selected outliner entry.</summary>
public abstract class DetailsViewModel : ViewModelBase
{
    public abstract string Title { get; }

    /// <summary>Re-reads the component, e.g. after the viewport moved the camera.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>Edits the scene's one camera. Cameras are adjusted, never added or removed.</summary>
public sealed partial class CameraDetailsViewModel(OrbitCameraComponent camera) : DetailsViewModel
{
    public override string Title => "Camera (orbit)";

    public double Yaw
    {
        get => camera.Settings.YawDegrees;
        set => camera.Settings = camera.Settings with { YawDegrees = (float)value };
    }

    public double Pitch
    {
        get => camera.Settings.PitchDegrees;
        set => camera.Settings = camera.Settings with { PitchDegrees = (float)value };
    }

    public double Distance
    {
        get => camera.Settings.Distance;
        set => camera.Settings = camera.Settings with { Distance = (float)value };
    }

    public double FieldOfView
    {
        get => camera.Settings.FieldOfViewDegrees;
        set => camera.Settings = camera.Settings with { FieldOfViewDegrees = (float)value };
    }

    public string Target => $"{camera.Settings.Target.X:0.###}, {camera.Settings.Target.Y:0.###}, {camera.Settings.Target.Z:0.###}";

    [RelayCommand]
    private void Reset() => camera.Settings = OrbitCameraSettings.Default;
}

/// <summary>Edits the scene's one directional light. Lights are adjusted, never added or removed.</summary>
public sealed partial class LightDetailsViewModel(DirectionalLightComponent light) : DetailsViewModel
{
    public override string Title => "Light (directional)";

    public double Azimuth
    {
        get => light.Settings.AzimuthDegrees;
        set => light.Settings = light.Settings with { AzimuthDegrees = (float)value };
    }

    public double Elevation
    {
        get => light.Settings.ElevationDegrees;
        set => light.Settings = light.Settings with { ElevationDegrees = (float)value };
    }

    public double Intensity
    {
        get => light.Settings.Intensity;
        set => light.Settings = light.Settings with { Intensity = (float)value };
    }

    [RelayCommand]
    private void Reset() => light.Settings = DirectionalLightSettings.Default;
}

/// <summary>Read-only facts about a mesh in the scene.</summary>
public sealed class MeshDetailsViewModel(SceneRoot scene, MeshComponent mesh) : DetailsViewModel
{
    public override string Title => mesh.Name;

    public string Source => scene.FindAsset(mesh).Bind(a => Graph.Maybe.From(a.SourcePath)).OrDefault("(none)");

    public string Geometry => scene.FindAsset(mesh).Bind(a => a.Geometry).Match(
        g => $"{g.VertexCount:N0} vertices, {g.TriangleCount:N0} triangles\nSize {g.Bounds.Size.X:0.###} × {g.Bounds.Size.Y:0.###} × {g.Bounds.Size.Z:0.###}",
        () => "Geometry missing: the source file could not be read.");
}
