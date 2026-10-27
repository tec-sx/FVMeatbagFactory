using System.Numerics;

namespace FVMeatbag.Rendering.Materials;

/// <summary>
/// Metallic-roughness PBR material, matching the glTF 2.0 core model. This is the common
/// denominator of UE5 (default lit), Substance Painter export presets and Blender's Principled BSDF.
/// </summary>
/// <remarks>
/// Factors multiply their texture, as in glTF. Textures are stored in the canonical conventions of
/// <see cref="TextureSlot"/>; exporters convert (e.g. flip normal green for Unreal) at the boundary.
/// </remarks>
public sealed record PbrMaterial
{
    public string Name { get; init; } = "Material";

    public Vector4 BaseColorFactor { get; init; } = Vector4.One;
    public float MetallicFactor { get; init; }
    public float RoughnessFactor { get; init; } = 0.5f;
    public float NormalScale { get; init; } = 1f;
    public float OcclusionStrength { get; init; } = 1f;
    public Vector3 EmissiveFactor { get; init; }

    public AlphaMode AlphaMode { get; init; } = AlphaMode.Opaque;
    public float AlphaCutoff { get; init; } = 0.5f;
    public bool DoubleSided { get; init; }

    public IReadOnlyDictionary<TextureSlot, string> Textures { get; init; } = new Dictionary<TextureSlot, string>();
}

public enum AlphaMode
{
    Opaque,
    Mask,
    Blend,
}

/// <summary>Texture slots and the canonical encoding each is stored in.</summary>
public enum TextureSlot
{
    /// <summary>sRGB RGB(A). Alpha is opacity.</summary>
    BaseColor,

    /// <summary>Linear tangent-space normal, OpenGL convention (+Y green), MikkTSpace tangents.</summary>
    Normal,

    /// <summary>Linear, packed R = ambient occlusion, G = roughness, B = metallic (glTF / UE "ORM").</summary>
    OcclusionRoughnessMetallic,

    /// <summary>sRGB RGB.</summary>
    Emissive,
}

/// <summary>Green-channel convention of a tangent-space normal map.</summary>
public enum NormalMapConvention
{
    /// <summary>+Y up. glTF, Blender, Substance "OpenGL" preset. Canonical inside the app.</summary>
    OpenGL,

    /// <summary>-Y. Unreal Engine, Substance "DirectX" preset. Flip green on import/export.</summary>
    DirectX,
}
