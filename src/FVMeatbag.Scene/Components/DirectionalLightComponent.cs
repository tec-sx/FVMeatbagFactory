using FVMeatbag.Core.Project;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.Scene.Components;

/// <summary>
/// The scene's one light, a directional (sun) light. Created by <see cref="SceneRoot"/>; it can be
/// adjusted but never added or removed.
/// </summary>
public sealed class DirectionalLightComponent : SceneComponent
{
    private DirectionalLightSettings _settings = DirectionalLightSettings.Default;

    internal DirectionalLightComponent() : base("Light")
    {
    }

    public override bool IsRemovable => false;

    /// <summary>Computes the shading inputs; exists while the light is attached.</summary>
    public Node<DirectionalLightInput, DirectionalLightOutput>? LightNode { get; private set; }

    public DirectionalLightSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value with { Intensity = MathF.Max(0f, value.Intensity) };
            LightNode?.UpdateInputs((ref DirectionalLightInput i) => i.Settings = _settings);
        }
    }

    protected override void OnAttached(Graph.Graph graph) =>
        LightNode = graph.AddNode(new DirectionalLightOperation(), $"{Name}.Shading", new DirectionalLightInput { Settings = _settings });

    protected override void OnDetaching() => LightNode = null;
}
