using System.Numerics;

namespace FVMeatbag.Core.Project;

/// <summary>
/// The project's one camera: it orbits <see cref="Target"/> at <see cref="Distance"/>. Yaw 0 looks
/// down -Z from the +Z side (the front of a glTF model); positive pitch looks down from above.
/// </summary>
public readonly record struct OrbitCameraSettings(
    Vector3 Target,
    float YawDegrees,
    float PitchDegrees,
    float Distance,
    float FieldOfViewDegrees)
{
    public const float MinPitch = -89f;
    public const float MaxPitch = 89f;
    public const float MinDistance = 0.01f;
    public const float MaxDistance = 1000f;

    public static OrbitCameraSettings Default { get; } = new(Vector3.Zero, 0f, 10f, 4f, 40f);

    public Vector3 Eye
    {
        get
        {
            var yaw = float.DegreesToRadians(YawDegrees);
            var pitch = float.DegreesToRadians(PitchDegrees);
            var offset = new Vector3(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(yaw));
            return Target + offset * Distance;
        }
    }

    /// <summary>Clamps pitch and distance into range and wraps yaw to (-180, 180].</summary>
    public OrbitCameraSettings Normalized()
    {
        var yaw = YawDegrees % 360f;
        yaw = yaw > 180f ? yaw - 360f : yaw <= -180f ? yaw + 360f : yaw;
        return this with
        {
            YawDegrees = yaw,
            PitchDegrees = Math.Clamp(PitchDegrees, MinPitch, MaxPitch),
            Distance = Math.Clamp(Distance, MinDistance, MaxDistance),
            FieldOfViewDegrees = Math.Clamp(FieldOfViewDegrees, 5f, 150f),
        };
    }
}

/// <summary>
/// The project's one light: a sun-like directional light. Azimuth is measured like the camera's
/// yaw (0 = from the front, +Z), elevation is the angle above the horizon.
/// </summary>
public readonly record struct DirectionalLightSettings(
    float AzimuthDegrees,
    float ElevationDegrees,
    Vector3 Color,
    float Intensity)
{
    public static DirectionalLightSettings Default { get; } = new(35f, 45f, Vector3.One, 2.5f);

    /// <summary>Unit vector pointing from the scene towards the light.</summary>
    public Vector3 DirectionToLight
    {
        get
        {
            var azimuth = float.DegreesToRadians(AzimuthDegrees);
            var elevation = float.DegreesToRadians(ElevationDegrees);
            return Vector3.Normalize(new Vector3(
                MathF.Cos(elevation) * MathF.Sin(azimuth),
                MathF.Sin(elevation),
                MathF.Cos(elevation) * MathF.Cos(azimuth)));
        }
    }

    /// <summary>Color times intensity, in linear units.</summary>
    public Vector3 Radiance => Color * Intensity;
}
