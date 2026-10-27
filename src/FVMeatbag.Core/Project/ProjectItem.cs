using FVMeatbag.Core.Import;
using FVMeatbag.Graph;

namespace FVMeatbag.Core.Project;

/// <summary>A node in the project hierarchy. The hierarchy owns the data; scene components only display it.</summary>
public abstract class ProjectItem
{
    private readonly List<ProjectItem> _children = [];

    protected ProjectItem(string name)
    {
        Name = name;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public ProjectItem? Parent { get; private set; }
    public IReadOnlyList<ProjectItem> Children => _children;

    public T Add<T>(T child) where T : ProjectItem
    {
        if (child.Parent is not null)
        {
            throw new InvalidOperationException($"{child.Name} already has a parent.");
        }
        _children.Add(child);
        child.Parent = this;
        return child;
    }

    public bool Remove(ProjectItem child)
    {
        if (!_children.Remove(child))
        {
            return false;
        }
        child.Parent = null;
        return true;
    }

    public IEnumerable<ProjectItem> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in _children)
        {
            foreach (var item in child.DescendantsAndSelf())
            {
                yield return item;
            }
        }
    }

    public override string ToString() => Name;
}

public sealed class ProjectFolder(string name) : ProjectItem(name);

/// <summary>
/// A skeletal mesh asset: geometry, and for rigged meshes the skeleton and skin weights. Morph
/// targets and poses are added as the pipeline grows.
/// </summary>
public sealed class SkeletalMeshAsset(string name, string? sourcePath = null) : ProjectItem(name)
{
    private Maybe<MeshGeometry> _geometry;
    private Maybe<Skeleton> _skeleton;

    /// <summary>Where the mesh was imported from: relative to the project directory when it was copied into the project.</summary>
    public string? SourcePath { get; } = sourcePath;

    /// <summary>The mesh, or None if its source could not be read.</summary>
    public Maybe<MeshGeometry> Geometry
    {
        get => _geometry;
        set
        {
            _geometry = value;
            MarkModified();
        }
    }

    /// <summary>The bones the geometry's skin weights refer to; None for a static mesh.</summary>
    public Maybe<Skeleton> Skeleton
    {
        get => _skeleton;
        set
        {
            _skeleton = value;
            MarkModified();
        }
    }

    /// <summary>Bumped on every edit, so graph change detection can compare versions instead of data.</summary>
    public long Version { get; private set; }

    public void MarkModified() => Version++;
}
