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
    /// Creates the projects folder and the demo project if they are missing, copying
    /// <paramref name="demoMeshes"/> into the new demo project and starting its camera at
    /// <paramref name="demoCamera"/>. An existing demo project is left alone. Returns the demo
    /// project's folder.
    /// </summary>
    public string EnsureCreated(IEnumerable<string>? demoMeshes = null, Maybe<OrbitCameraSettings> demoCamera = default)
    {
        Directory.CreateDirectory(Root);
        if (!ProjectFile.Exists(DemoDirectory))
        {
            var demo = new CharacterProject(DemoProjectName, DemoDirectory)
            {
                Camera = demoCamera.OrDefault(OrbitCameraSettings.Default),
            };
            foreach (var mesh in demoMeshes ?? [])
            {
                demo.Meshes.Add(new SkeletalMeshAsset(Path.GetFileNameWithoutExtension(mesh), ProjectFile.CopyIntoProject(demo, mesh)));
            }
            ProjectFile.Save(demo);
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
}
