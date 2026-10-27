namespace FVMeatbag.Scene.Components;

/// <summary>
/// Displays a skeletal mesh that lives in the project hierarchy. It holds only a reference to the
/// asset; geometry is read from the hierarchy by nodes in this component's graph (next milestone).
/// </summary>
public sealed class MeshComponent(string name, Guid assetId) : SceneComponent(name)
{
    public Guid AssetId { get; } = assetId;
}
