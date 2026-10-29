using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Project;
using FVMeatbag.Core.Rig;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Components;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.Scene.Tests;

public sealed class RigTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>The Manny body (optional bones included unless left out) plus extras as (name, parent).</summary>
    internal static Skeleton MannyWith(IEnumerable<(string Name, string Parent)>? extras = null, bool withOptional = true)
    {
        var bones = new List<Bone>();
        var index = new Dictionary<string, int>();
        void Add(string name, int parent)
        {
            index[name] = bones.Count;
            bones.Add(new Bone(name, parent, Matrix4x4.CreateTranslation(0, 0.1f, 0)));
        }
        foreach (var bone in MannySkeleton.Bones.Where(b => withOptional || b.Kind == MannyBoneKind.Required))
        {
            Add(bone.Name, bone.Parent.Match(p => index[p], () => -1));
        }
        foreach (var (name, parent) in extras ?? [])
        {
            Add(name, parent.Length == 0 ? -1 : index[parent]);
        }
        return new Skeleton(bones);
    }

    [Fact]
    public void The_manny_body_conforms_and_extras_hang_below_it()
    {
        var skeleton = MannyWith([("bicep_l", "upperarm_l"), ("bicep_hi_l", "bicep_l"), ("FACIAL_jaw", "head")]);

        var check = MannyRig.Check(skeleton);

        Assert.True(check.IsConformant, string.Join("\n", check.Problems.Select(p => p.Message)));
        var rig = check.Rig.Value;
        Assert.Equal(MannySkeleton.Bones.Count, rig.CoreCount);
        Assert.Equal(["bicep_l", "bicep_hi_l", "FACIAL_jaw"], rig.Extras.Select(b => skeleton.Bones[b].Name));
        Assert.Equal(rig.Core("upperarm_l").Value, rig.DriverOf(skeleton.FindBone("bicep_hi_l").Value));
        Assert.Equal(rig.Core("UpperArm_L"), rig.Core("upperarm_l")); // UE compares names ignoring case
        Assert.Contains("extras", check.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Twist_and_ik_bones_are_optional()
    {
        var check = MannyRig.Check(MannyWith(withOptional: false));

        Assert.True(check.IsConformant);
        Assert.True(check.Rig.Value.Core("upperarm_twist_01_l").IsNone);
    }

    [Fact]
    public void Problems_name_each_bone_that_breaks_the_contract()
    {
        var bones = MannyWith().Bones.ToList();
        var head = bones.FindIndex(b => b.Name == "head");
        bones[head] = bones[head] with { Name = "skull" };
        var spine = bones.FindIndex(b => b.Name == "spine_01");
        var thigh = bones.FindIndex(b => b.Name == "thigh_l");
        bones[thigh] = bones[thigh] with { Parent = spine };
        bones.Add(new Bone("prop", -1, Matrix4x4.Identity));
        bones.Add(new Bone("PROP", -1, Matrix4x4.Identity));

        var check = MannyRig.Check(new Skeleton(bones));

        Assert.False(check.IsConformant);
        Assert.True(check.Rig.IsNone);
        Assert.Contains(check.Problems, p => p is { Kind: MannyProblemKind.Missing, Bone: "head" });
        Assert.Contains(check.Problems, p => p.Kind == MannyProblemKind.WrongParent && p.Message == "thigh_l should be a child of pelvis, but it is under spine_01.");
        Assert.Contains(check.Problems, p => p is { Kind: MannyProblemKind.DuplicateName, Bone: "PROP" });
        Assert.Contains(check.Problems, p => p is { Kind: MannyProblemKind.ExtraRoot, Bone: "prop" });
    }

    [Theory]
    [InlineData("bicep_l", "bicep_r")]
    [InlineData("hand.L", "hand.R")]
    [InlineData("L_clavicle", "R_clavicle")]
    [InlineData("mixamorig:LeftHand", "mixamorig:RightHand")]
    [InlineData("Quad_Upper_R", "Quad_Upper_L")]
    [InlineData("LEFT_eye", "RIGHT_eye")]
    [InlineData("spine_01", "spine_01")]
    public void Mirror_swaps_the_side_marker(string name, string mirrored) => Assert.Equal(mirrored, BoneNames.Mirror(name));

    [Theory]
    [InlineData("shoulder_l", "shoulder", BoneSide.Left, null)]
    [InlineData("bicep_l", "bicep", BoneSide.Left, null)]
    [InlineData("trapezius_l", "trapezius", BoneSide.Left, null)]
    [InlineData("pectoralis_l", "pectoralis", BoneSide.Left, null)]
    [InlineData("gluteus_l", "gluteus", BoneSide.Left, null)]
    [InlineData("hip_flexor_l", "hip_flexor", BoneSide.Left, null)]
    [InlineData("quadriceps_l", "quadriceps", BoneSide.Left, null)]
    [InlineData("gastrocnemius_l", "gastrocnemius", BoneSide.Left, null)]
    [InlineData("achilles_tendon_l", "achilles_tendon", BoneSide.Left, null)]
    [InlineData("biceps-hi_r", "bicep", BoneSide.Right, "hi")]
    [InlineData("Quad_Upper_R", "quadriceps", BoneSide.Right, "upper")]
    [InlineData("hipFlexor_02_L", "hip_flexor", BoneSide.Left, "02")]
    [InlineData("shoulder_blade_l", "scapula", BoneSide.Left, null)]
    [InlineData("abs_01_l", "abdominals", BoneSide.Center, "01")]
    public void Roles_are_suggested_from_bone_names(string name, string role, BoneSide side, string? part)
    {
        var tag = CorrectiveRoleCatalog.Default.Suggest(name).Value;

        Assert.Equal(role, tag.Role);
        Assert.Equal(side, tag.Side);
        Assert.Equal(Maybe.From(part), tag.Part);
    }

    [Fact]
    public void Unknown_names_get_no_suggestion()
    {
        Assert.True(CorrectiveRoleCatalog.Default.Suggest("FACIAL_jaw").IsNone);
        Assert.True(CorrectiveRoleCatalog.Default.Suggest("weapon_r").IsNone);
    }

    [Fact]
    public void Every_role_parent_is_a_manny_bone()
    {
        Assert.NotEmpty(CorrectiveRoleCatalog.Default.Roles);
        Assert.All(CorrectiveRoleCatalog.Default.Roles, r =>
            Assert.True(MannySkeleton.IsCore(r.Parent) || MannySkeleton.IsCore(r.Parent + "_l"), $"{r.Id}: {r.Parent}"));
    }

    [Fact]
    public void Suggest_all_tags_untagged_extras_and_skips_ignored_and_taken_tags()
    {
        var skeleton = MannyWith([("bicep_l", "upperarm_l"), ("biceps_l", "upperarm_l"), ("gluteus_l", "pelvis"), ("quad_l", "thigh_l"), ("socket_r", "hand_r")]);
        var rig = MannyRig.Check(skeleton).Rig.Value;
        var setup = new CorrectiveSetup();
        setup.SetMode("quad_l", CorrectiveMode.Ignore);

        var tagged = setup.SuggestAll(rig, CorrectiveRoleCatalog.Default);

        Assert.Equal(2, tagged);
        Assert.Equal("bicep_l", setup["bicep_l"].Tag.Value.CanonicalName);
        Assert.True(setup["biceps_l"].Tag.IsNone); // bicep_l already has that tag
        Assert.Equal("gluteus_l", setup["gluteus_l"].Tag.Value.CanonicalName);
        Assert.True(setup["quad_l"].Tag.IsNone);
        Assert.True(setup["socket_r"].Tag.IsNone);
        Assert.Equal(skeleton.FindBone("gluteus_l"), setup.FindBone(rig, new CorrectiveTag("gluteus", BoneSide.Left, Maybe<string>.None)));
    }

    [Fact]
    public void Mirroring_copies_the_binding_with_the_side_flipped()
    {
        var skeleton = MannyWith([("muscle_a_l", "upperarm_l"), ("muscle_a_r", "upperarm_r")]);
        var rig = MannyRig.Check(skeleton).Rig.Value;
        var setup = new CorrectiveSetup();
        setup.Set("muscle_a_l", new CorrectiveBinding(new CorrectiveTag("bicep", BoneSide.Left, "hi"), CorrectiveMode.Lock));

        var other = setup.Mirror(rig, skeleton.FindBone("muscle_a_l").Value);

        Assert.Equal(skeleton.FindBone("muscle_a_r"), other);
        Assert.Equal(new CorrectiveBinding(new CorrectiveTag("bicep", BoneSide.Right, "hi"), CorrectiveMode.Lock), setup["muscle_a_r"]);
    }

    [Fact]
    public void Renaming_keeps_names_unique_and_moves_the_binding()
    {
        var skeleton = MannyWith([("muscle_a_l", "upperarm_l")]);
        var asset = new SkeletalMeshAsset("Body") { Skeleton = skeleton };
        var bone = skeleton.FindBone("muscle_a_l").Value;
        asset.Correctives.SetMode("muscle_a_l", CorrectiveMode.Lock);

        Assert.False(asset.RenameBone(bone, "HEAD"));
        Assert.True(asset.RenameBone(bone, " bicep_hi_l "));

        Assert.Equal("bicep_hi_l", skeleton.Bones[bone].Name);
        Assert.Equal("muscle_a_l", skeleton.SourceName(bone));
        Assert.Equal(1, skeleton.Version);
        Assert.Equal(CorrectiveMode.Lock, asset.Correctives["bicep_hi_l"].Mode);
        Assert.False(asset.Correctives.Bindings.ContainsKey("muscle_a_l"));
    }

    [Fact]
    public void Renames_and_correctives_are_saved_with_the_project()
    {
        var directory = _temp.Combine("Projects", "Arm");
        var project = new CharacterProject("Arm", directory);
        var path = ProjectFile.CopyIntoProject(project, TestSkinnedMeshes.WriteBentArm(_temp.Combine("Arm.glb"), Vector3.Zero));
        var mesh = GltfImporter.Load(_temp.Combine("Projects", "Arm", path));
        var asset = project.Meshes.Add(new SkeletalMeshAsset("Arm", path) { Geometry = mesh.Geometry, Skeleton = mesh.Skeleton });
        Assert.True(asset.RenameBone(1, "bicep_hi_l"));
        var posed = BoneTransform.Identity with { Translation = new Vector3(0, 2, 0) };
        asset.Pose.Value.Set(1, posed);
        asset.Correctives.Set("bicep_hi_l", new CorrectiveBinding(new CorrectiveTag("bicep", BoneSide.Left, "hi"), CorrectiveMode.Lock));
        asset.Correctives.SetMode("Lower", CorrectiveMode.Ignore);
        ProjectFile.Save(project);

        var loaded = Assert.Single(ProjectFile.Load(directory).MeshAssets);

        var skeleton = loaded.Skeleton.Value;
        Assert.Equal(["Lower", "bicep_hi_l"], skeleton.Bones.Select(b => b.Name));
        Assert.Equal("Upper", skeleton.SourceName(1));
        Assert.Equal(posed.Translation, loaded.Pose.Value[1].Translation);
        Assert.Equal(asset.Correctives["bicep_hi_l"], loaded.Correctives["bicep_hi_l"]);
        Assert.Equal(CorrectiveBinding.Default with { Mode = CorrectiveMode.Ignore }, loaded.Correctives["Lower"]);
    }

    [Fact]
    public void The_skeleton_graph_rechecks_the_rig_after_a_rename()
    {
        var skeleton = MannyWith([("muscle_l", "upperarm_l")]);
        var root = Graph.Graph.CreateRoot();
        var scene = new SceneRoot(root);
        var component = scene.Root.AddChild(new SkeletonComponent(skeleton));
        Assert.True(component.RigCheckNode!.OutputValues.Rig.HasValue);

        Assert.True(skeleton.Rename(skeleton.FindBone("head").Value, "Head2"));

        var check = component.RigCheckNode.OutputValues.Check.Value;
        Assert.False(check.IsConformant);
        Assert.Contains(check.Problems, p => p is { Kind: MannyProblemKind.Missing, Bone: "head" });
    }
}
