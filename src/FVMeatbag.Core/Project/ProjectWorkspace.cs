using FVMeatbag.Graph;

namespace FVMeatbag.Core.Project;

/// <summary>
/// The folder that holds the user's projects, <c>Documents/Projects</c> by default. Like an
/// installer, the first run creates it together with a <see cref="DemoProjectName"/> project.
/// </summary>
public sealed class ProjectWorkspace(string root)
{
    public const string DemoProjectName = "Demo";

    public string Root { get; } = Path.GetFullPath(root);

    public string DemoDirectory => Path.Combine(Root, DemoProjectName);

    /// <summary>Documents/Projects for the current user.</summary>
    public static ProjectWorkspace ForCurrentUser()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents))
        {
            documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
        }
        return new ProjectWorkspace(Path.Combine(documents, "Projects"));
    }

    /// <summary>
    /// Creates the projects folder and the demo project if they are missing and returns the demo
    /// project's folder. <paramref name="demoTemplate"/> is a saved project folder (the app ships
    /// one with the sample character) that is copied in as the demo. An existing demo project is
    /// left alone, unless it has no meshes and a template is given: then it is filled from the
    /// template, which upgrades an empty demo made by an earlier build.
    /// </summary>
    public string EnsureCreated(Maybe<string> demoTemplate = default)
    {
        Directory.CreateDirectory(Root);
        var exists = ProjectFile.Exists(DemoDirectory);
        if (demoTemplate.TryGetValue(out var template) && ProjectFile.Exists(template))
        {
            if (!exists || !ProjectFile.HasMeshes(DemoDirectory))
            {
                CopyFolder(template, DemoDirectory);
            }
        }
        else if (!exists)
        {
            ProjectFile.Save(new CharacterProject(DemoProjectName, DemoDirectory));
        }
        return DemoDirectory;
    }

    /// <summary>A folder for a new project called <paramref name="name"/> that does not exist yet.</summary>
    public string NewProjectDirectory(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string([.. name.Trim().Select(c => invalid.Contains(c) ? '_' : c)]);
        if (string.IsNullOrWhiteSpace(safe) || safe is "." or "..")
        {
            safe = "Project";
        }
        var directory = Path.Combine(Root, safe);
        for (var i = 2; Directory.Exists(directory); i++)
        {
            directory = Path.Combine(Root, $"{safe} {i}");
        }
        return directory;
    }

    /// <summary>Copies a folder's files and subfolders, replacing files that already exist.</summary>
    private static void CopyFolder(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }
}
