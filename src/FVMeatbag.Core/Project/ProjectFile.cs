using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Muscles;
using FVMeatbag.Core.Rig;
using FVMeatbag.Graph;

namespace FVMeatbag.Core.Project;

/// <summary>
/// Saves and opens projects. A project is a folder holding <see cref="FileName"/> and a
/// <c>Meshes</c> folder with the imported source files, so the folder can be copied, zipped or
/// committed as a whole.
/// </summary>
public static class ProjectFile
{
    public const string FileName = "project.json";
    public const string MeshesFolder = "Meshes";
    private const int CurrentFormat = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool Exists(string directory) => File.Exists(Path.Combine(directory, FileName));

    public static void Save(CharacterProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var directory = project.Directory.OrThrow($"{project.Name} has no folder to save to.");
        System.IO.Directory.CreateDirectory(directory);

        var document = new ProjectDocument
        {
            Format = CurrentFormat,
            Name = project.Name,
            Camera = CameraDocument.From(project.Camera),
            Light = LightDocument.From(project.Light),
            Meshes = [.. project.MeshAssets.Select(MeshDocument.From)],
        };
        var path = Path.Combine(directory, FileName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(document, Options));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Opens the project in <paramref name="directory"/> and imports its meshes. A mesh whose file is
    /// missing or unreadable stays in the project with no geometry and is reported in <paramref name="problems"/>.
    /// </summary>
    public static CharacterProject Load(string directory, ICollection<string>? problems = null)
    {
        var path = Path.Combine(directory, FileName);
        var document = JsonSerializer.Deserialize<ProjectDocument>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"{path} is empty.");
        if (document.Format > CurrentFormat)
        {
            throw new InvalidDataException($"{path} was written by a newer version (format {document.Format}).");
        }

        var project = new CharacterProject(document.Name ?? Path.GetFileName(directory), Maybe.Some(directory))
        {
            Camera = document.Camera?.ToSettings() ?? OrbitCameraSettings.Default,
            Light = document.Light?.ToSettings() ?? DirectionalLightSettings.Default,
        };
        foreach (var mesh in document.Meshes ?? [])
        {
            var asset = project.Meshes.Add(new SkeletalMeshAsset(mesh.Name ?? "Mesh", mesh.Path));
            var loaded = project.ResolvePath(mesh.Path).Bind(file => TryImport(file, problems));
            asset.Geometry = loaded.Map(l => l.Geometry);
            asset.Skeleton = loaded.Bind(l => l.Skeleton);
            if (asset.Skeleton.TryGetValue(out var skeleton))
            {
                mesh.ApplyRenames(asset, skeleton, problems);
            }
            if (asset.Pose.TryGetValue(out var pose))
            {
                mesh.ApplyPose(pose, problems);
            }
            mesh.ApplyCorrectives(asset.Correctives);
            mesh.ApplyMuscles(asset.Muscles, problems);
        }
        return project;
    }

    /// <summary>True if the project in <paramref name="directory"/> lists at least one mesh. Reads only the project file.</summary>
    public static bool HasMeshes(string directory)
    {
        var path = Path.Combine(directory, FileName);
        var document = JsonSerializer.Deserialize<ProjectDocument>(File.ReadAllText(path), Options);
        return document?.Meshes is { Count: > 0 };
    }

    /// <summary>
    /// Copies a source file into the project's <c>Meshes</c> folder and returns the path to store on
    /// the asset. Projects without a folder keep the original absolute path.
    /// </summary>
    public static string CopyIntoProject(CharacterProject project, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!project.Directory.TryGetValue(out var directory))
        {
            return Path.GetFullPath(sourcePath);
        }
        var meshes = System.IO.Directory.CreateDirectory(Path.Combine(directory, MeshesFolder)).FullName;
        var source = Path.GetFullPath(sourcePath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Path.GetDirectoryName(source) is { } sourceFolder && string.Equals(Path.GetFullPath(sourceFolder), meshes, comparison))
        {
            return $"{MeshesFolder}/{Path.GetFileName(source)}"; // already in the project
        }

        var name = Path.GetFileNameWithoutExtension(source);
        var extension = Path.GetExtension(source);
        var target = Path.Combine(meshes, name + extension);
        for (var i = 2; File.Exists(target); i++)
        {
            target = Path.Combine(meshes, $"{name}_{i}{extension}");
        }
        File.Copy(source, target);
        return $"{MeshesFolder}/{Path.GetFileName(target)}";
    }

    private static Maybe<LoadedMesh> TryImport(string file, ICollection<string>? problems)
    {
        try
        {
            return MeshImporter.Load(file);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or SharpGLTF.Validation.ModelException)
        {
            problems?.Add($"{Path.GetFileName(file)}: {e.Message}");
            return Maybe<LoadedMesh>.None;
        }
    }

    private sealed class ProjectDocument
    {
        public int Format { get; set; }
        public string? Name { get; set; }
        public CameraDocument? Camera { get; set; }
        public LightDocument? Light { get; set; }
        public List<MeshDocument>? Meshes { get; set; }
    }

    private sealed class MeshDocument
    {
        public string? Name { get; set; }
        public string? Path { get; set; }

        /// <summary>Bones renamed in the app, by the name they were imported with.</summary>
        public List<BoneRenameDocument>? Renames { get; set; }

        /// <summary>Posed bones only; bones at rest are left out.</summary>
        public List<BonePoseDocument>? Pose { get; set; }

        /// <summary>Corrective bones with a role or a non-default mode.</summary>
        public List<CorrectiveDocument>? Correctives { get; set; }

        public List<MuscleDocument>? Muscles { get; set; }

        public static MeshDocument From(SkeletalMeshAsset asset)
        {
            var posed = asset.Pose.Match(
                p => Enumerable.Range(0, p.Skeleton.Count).Where(p.IsPosed).Select(b => BonePoseDocument.From(p, b)).ToList(),
                () => []);
            var renames = asset.Skeleton.Match(
                s => Enumerable.Range(0, s.Count)
                    .Where(b => s.SourceName(b) != s.Bones[b].Name)
                    .Select(b => new BoneRenameDocument { Source = s.SourceName(b), Name = s.Bones[b].Name })
                    .ToList(),
                () => []);
            var correctives = asset.Correctives.Bindings
                .OrderBy(b => b.Key, StringComparer.OrdinalIgnoreCase)
                .Select(b => CorrectiveDocument.From(b.Key, b.Value))
                .ToList();
            return new MeshDocument
            {
                Name = asset.Name,
                Path = asset.SourcePath?.Replace('\\', '/'),
                Renames = renames.Count > 0 ? renames : null,
                Pose = posed.Count > 0 ? posed : null,
                Correctives = correctives.Count > 0 ? correctives : null,
                Muscles = asset.Muscles.Count > 0 ? [.. asset.Muscles.Muscles.Select(MuscleDocument.From)] : null,
            };
        }

        public void ApplyRenames(SkeletalMeshAsset asset, Skeleton skeleton, ICollection<string>? problems)
        {
            foreach (var rename in Renames ?? [])
            {
                var bone = Enumerable.Range(0, skeleton.Count).FirstOrNone(b => skeleton.SourceName(b) == rename.Source);
                if (!bone.TryGetValue(out var index) || string.IsNullOrWhiteSpace(rename.Name))
                {
                    problems?.Add($"{Name}: a rename names a bone the skeleton lacks ({rename.Source}).");
                }
                else if (!asset.RenameBone(index, rename.Name))
                {
                    problems?.Add($"{Name}: cannot rename {rename.Source} to {rename.Name}; another bone has that name.");
                }
            }
        }

        public void ApplyCorrectives(CorrectiveSetup setup)
        {
            foreach (var corrective in Correctives ?? [])
            {
                if (!string.IsNullOrWhiteSpace(corrective.Bone))
                {
                    setup.Set(corrective.Bone, corrective.ToBinding());
                }
            }
        }

        public void ApplyMuscles(MuscleSet muscles, ICollection<string>? problems)
        {
            foreach (var muscle in Muscles ?? [])
            {
                if (muscle.ToMuscle().TryGetValue(out var loaded))
                {
                    muscles.Set(loaded);
                }
                else
                {
                    problems?.Add($"{Name}: a muscle has no name or bones ({muscle.Name}).");
                }
            }
        }

        public void ApplyPose(SkeletonPose pose, ICollection<string>? problems)
        {
            foreach (var bone in Pose ?? [])
            {
                if (pose.Skeleton.FindBone(bone.Bone ?? "").TryGetValue(out var index))
                {
                    pose.Set(index, bone.ToTransform());
                }
                else
                {
                    problems?.Add($"{Name}: the pose names a bone the skeleton lacks ({bone.Bone}).");
                }
            }
        }
    }

    private sealed class MuscleDocument
    {
        public string? Name { get; set; }
        public string? Template { get; set; }
        public string? Side { get; set; }
        public AttachmentDocument? Origin { get; set; }
        public AttachmentDocument? Insertion { get; set; }
        public float[]? Facing { get; set; }
        public string? Shape { get; set; }
        public string? Action { get; set; }
        public float Radius { get; set; }
        public float Flatness { get; set; }
        public float Thickness { get; set; }
        public float Volume { get; set; }
        public float[]? Tendons { get; set; }
        public List<DriveDocument>? Drives { get; set; }

        public static MuscleDocument From(Muscle muscle) => new()
        {
            Name = muscle.Name,
            Template = muscle.Template.Match(t => t, () => (string?)null),
            Side = muscle.Side.ToString().ToLowerInvariant(),
            Origin = AttachmentDocument.From(muscle.Origin),
            Insertion = AttachmentDocument.From(muscle.Insertion),
            Facing = [muscle.Facing.X, muscle.Facing.Y, muscle.Facing.Z],
            Shape = muscle.Shape.ToString().ToLowerInvariant(),
            Action = muscle.Action.ToString().ToLowerInvariant(),
            Radius = muscle.Radius,
            Flatness = muscle.Flatness,
            Thickness = muscle.Thickness,
            Volume = muscle.Volume,
            Tendons = [muscle.Tendons.X, muscle.Tendons.Y],
            Drives = muscle.Drives.Count > 0 ? [.. muscle.Drives.Select(DriveDocument.From)] : null,
        };

        public Maybe<Muscle> ToMuscle()
        {
            if (string.IsNullOrWhiteSpace(Name) || Origin?.ToAttachment() is not { } origin || Insertion?.ToAttachment() is not { } insertion)
            {
                return Maybe<Muscle>.None;
            }
            return new Muscle(
                Name,
                string.IsNullOrWhiteSpace(Template) ? Maybe<string>.None : Template,
                Enum.TryParse<BoneSide>(Side, ignoreCase: true, out var side) ? side : BoneSide.Center,
                origin,
                insertion,
                Facing is [var fx, var fy, var fz] ? new Vector3(fx, fy, fz) : Vector3.UnitY,
                Enum.TryParse<MuscleShape>(Shape, ignoreCase: true, out var shape) ? shape : MuscleShape.Fusiform,
                Enum.TryParse<MuscleAction>(Action, ignoreCase: true, out var action) ? action : MuscleAction.Flexor,
                Radius,
                Flatness,
                Thickness,
                Volume,
                Tendons is [var a, var b] ? new Vector2(a, b) : new Vector2(0.1f, 0.1f),
                [.. (Drives ?? []).Where(d => !string.IsNullOrWhiteSpace(d.Role)).Select(d => d.ToDrive())]);
        }
    }

    private sealed class AttachmentDocument
    {
        public string? Bone { get; set; }
        public float[]? Local { get; set; }

        public static AttachmentDocument From(MuscleAttachment attachment) => new()
        {
            Bone = attachment.Bone,
            Local = [attachment.Local.X, attachment.Local.Y, attachment.Local.Z],
        };

        public MuscleAttachment? ToAttachment() =>
            string.IsNullOrWhiteSpace(Bone) ? null : new MuscleAttachment(Bone, Local is [var x, var y, var z] ? new Vector3(x, y, z) : Vector3.Zero);
    }

    private sealed class DriveDocument
    {
        public string? Role { get; set; }
        public string? Part { get; set; }
        public float Weight { get; set; }

        public static DriveDocument From(MuscleDrive drive) => new()
        {
            Role = drive.Role,
            Part = drive.Part.Match(p => p, () => (string?)null),
            Weight = drive.Weight,
        };

        public MuscleDrive ToDrive() => new(Role!, string.IsNullOrWhiteSpace(Part) ? Maybe<string>.None : Part, Weight);
    }

    private sealed class BoneRenameDocument
    {
        public string? Source { get; set; }
        public string? Name { get; set; }
    }

    private sealed class CorrectiveDocument
    {
        public string? Bone { get; set; }
        public string? Role { get; set; }
        public string? Side { get; set; }
        public string? Part { get; set; }

        /// <summary>Left out for <see cref="CorrectiveMode.Solve"/>.</summary>
        public string? Mode { get; set; }

        public static CorrectiveDocument From(string bone, CorrectiveBinding binding) => new()
        {
            Bone = bone,
            Role = binding.Tag.Match(t => t.Role, () => (string?)null),
            Side = binding.Tag.Match(t => t.Side.ToString().ToLowerInvariant(), () => (string?)null),
            Part = binding.Tag.Bind(t => t.Part).Match(p => p, () => (string?)null),
            Mode = binding.Mode == CorrectiveMode.Solve ? null : binding.Mode.ToString().ToLowerInvariant(),
        };

        public CorrectiveBinding ToBinding()
        {
            var tag = string.IsNullOrWhiteSpace(Role)
                ? Maybe<CorrectiveTag>.None
                : new CorrectiveTag(Role, Enum.TryParse<BoneSide>(Side, ignoreCase: true, out var side) ? side : BoneSide.Center,
                    string.IsNullOrWhiteSpace(Part) ? Maybe<string>.None : Part);
            var mode = Enum.TryParse<CorrectiveMode>(Mode, ignoreCase: true, out var m) ? m : CorrectiveMode.Solve;
            return new CorrectiveBinding(tag, mode);
        }
    }

    private sealed class BonePoseDocument
    {
        public string? Bone { get; set; }
        public float[]? Translation { get; set; }

        /// <summary>Quaternion x, y, z, w.</summary>
        public float[]? Rotation { get; set; }

        public float[]? Scale { get; set; }

        public static BonePoseDocument From(SkeletonPose pose, int bone)
        {
            var t = pose[bone];
            return new BonePoseDocument
            {
                Bone = pose.Skeleton.Bones[bone].Name,
                Translation = [t.Translation.X, t.Translation.Y, t.Translation.Z],
                Rotation = [t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W],
                Scale = [t.Scale.X, t.Scale.Y, t.Scale.Z],
            };
        }

        public BoneTransform ToTransform() => new(
            ToVector(Translation, Vector3.Zero),
            Rotation is { Length: 4 } r ? Quaternion.Normalize(new Quaternion(r[0], r[1], r[2], r[3])) : Quaternion.Identity,
            ToVector(Scale, Vector3.One));
    }

    private sealed class CameraDocument
    {
        public float[]? Target { get; set; }
        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public float Distance { get; set; }
        public float FieldOfView { get; set; }

        public static CameraDocument From(OrbitCameraSettings s) => new()
        {
            Target = [s.Target.X, s.Target.Y, s.Target.Z],
            Yaw = s.YawDegrees,
            Pitch = s.PitchDegrees,
            Distance = s.Distance,
            FieldOfView = s.FieldOfViewDegrees,
        };

        public OrbitCameraSettings ToSettings() =>
            new OrbitCameraSettings(ToVector(Target, Vector3.Zero), Yaw, Pitch, Distance, FieldOfView).Normalized();
    }

    private sealed class LightDocument
    {
        public float Azimuth { get; set; }
        public float Elevation { get; set; }
        public float[]? Color { get; set; }
        public float Intensity { get; set; }

        public static LightDocument From(DirectionalLightSettings s) => new()
        {
            Azimuth = s.AzimuthDegrees,
            Elevation = s.ElevationDegrees,
            Color = [s.Color.X, s.Color.Y, s.Color.Z],
            Intensity = s.Intensity,
        };

        public DirectionalLightSettings ToSettings() => new(Azimuth, Elevation, ToVector(Color, Vector3.One), Intensity);
    }

    private static Vector3 ToVector(float[]? values, Vector3 fallback) =>
        values is { Length: 3 } ? new Vector3(values[0], values[1], values[2]) : fallback;
}
