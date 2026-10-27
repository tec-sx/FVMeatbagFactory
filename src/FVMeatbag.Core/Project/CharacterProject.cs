using FVMeatbag.Graph;

namespace FVMeatbag.Core.Project;

/// <summary>The root of a project hierarchy.</summary>
public sealed class CharacterProject
{
    public CharacterProject(string name)
    {
        Name = name;
        Root = new ProjectFolder(name);
        Meshes = Root.Add(new ProjectFolder("Meshes"));
        Morphs = Root.Add(new ProjectFolder("Morphs"));
        Poses = Root.Add(new ProjectFolder("Poses"));
    }

    public string Name { get; }
    public ProjectFolder Root { get; }
    public ProjectFolder Meshes { get; }
    public ProjectFolder Morphs { get; }
    public ProjectFolder Poses { get; }

    public Maybe<ProjectItem> Find(Guid id) => Root.DescendantsAndSelf().FirstOrNone(i => i.Id == id);
}
