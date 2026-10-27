using FVMeatbag.Graph;

namespace FVMeatbag.Core.Project;

/// <summary>The root of a project hierarchy.</summary>
public sealed class CharacterProject
{
    public CharacterProject(string name, Maybe<string> directory = default)
    {
        Name = name;
        Directory = directory;
        Root = new ProjectFolder(name);
        Meshes = Root.Add(new ProjectFolder("Meshes"));
        Morphs = Root.Add(new ProjectFolder("Morphs"));
        Poses = Root.Add(new ProjectFolder("Poses"));
    }

    public string Name { get; }

    /// <summary>The folder the project is saved in, or None for a project that only lives in memory.</summary>
    public Maybe<string> Directory { get; }

    public ProjectFolder Root { get; }
    public ProjectFolder Meshes { get; }
    public ProjectFolder Morphs { get; }
    public ProjectFolder Poses { get; }

    /// <summary>
    /// Every project has exactly one camera and one light. They are adjusted, never added or
    /// removed, because the app is about the character rather than staging a scene.
    /// </summary>
    public OrbitCameraSettings Camera { get; set; } = OrbitCameraSettings.Default;

    public DirectionalLightSettings Light { get; set; } = DirectionalLightSettings.Default;

    public Maybe<ProjectItem> Find(Guid id) => Root.DescendantsAndSelf().FirstOrNone(i => i.Id == id);

    public IEnumerable<SkeletalMeshAsset> MeshAssets => Meshes.DescendantsAndSelf().OfType<SkeletalMeshAsset>();

    /// <summary>Resolves an asset's stored path against the project directory.</summary>
    public Maybe<string> ResolvePath(string? path) =>
        Maybe.From(path).Map(p => Path.IsPathRooted(p) ? p : Path.Combine(Directory.OrDefault(""), p));
}
