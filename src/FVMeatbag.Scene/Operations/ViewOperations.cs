using System.Numerics;
using FVMeatbag.Core.Project;
using FVMeatbag.Graph;

namespace FVMeatbag.Scene.Operations;

public struct OrbitCameraInput
{
    public OrbitCameraSettings Settings;

    /// <summary>Viewport width / height, set by the viewport when it resizes.</summary>
    public float AspectRatio;
}

public struct OrbitCameraOutput
{
    public Matrix4x4 View;

    /// <summary>Right-handed, depth 0..1, in System.Numerics row-vector convention. Backends adjust for their clip space.</summary>
    public Matrix4x4 Projection;

    public Vector3 Eye;
}

/// <summary>Turns orbit settings into view and projection matrices.</summary>
public sealed class OrbitCameraOperation : Operation<OrbitCameraInput, OrbitCameraOutput>
{
    public const float NearPlane = 0.01f;
    public const float FarPlane = 1000f;

    public override void Execute(in OrbitCameraInput input, ref OrbitCameraOutput output, EvaluationContext context)
    {
        var settings = input.Settings.Normalized();
        var aspect = input.AspectRatio > 0f ? input.AspectRatio : 1f;
        output.Eye = settings.Eye;
        output.View = Matrix4x4.CreateLookAt(output.Eye, settings.Target, Vector3.UnitY);
        output.Projection = Matrix4x4.CreatePerspectiveFieldOfView(
            float.DegreesToRadians(settings.FieldOfViewDegrees), aspect, NearPlane, FarPlane);
    }
}

public struct DirectionalLightInput
{
    public DirectionalLightSettings Settings;
}

public struct DirectionalLightOutput
{
    /// <summary>Unit vector from the scene towards the light, in world space.</summary>
    public Vector3 DirectionToLight;

    /// <summary>Color × intensity.</summary>
    public Vector3 Radiance;
}

/// <summary>Turns light settings into the values a shader needs.</summary>
public sealed class DirectionalLightOperation : Operation<DirectionalLightInput, DirectionalLightOutput>
{
    public override void Execute(in DirectionalLightInput input, ref DirectionalLightOutput output, EvaluationContext context)
    {
        output.DirectionToLight = input.Settings.DirectionToLight;
        output.Radiance = input.Settings.Radiance;
    }
}
