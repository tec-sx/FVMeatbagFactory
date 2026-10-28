using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Project;
using FVMeatbag.Core.Rig;
using FVMeatbag.Graph;
using FVMeatbag.Scene.Components;
using FVMeatbag.Scene.Operations;

namespace FVMeatbag.Scene.Tests;

public sealed class BoneDisplayTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("hand.L", BoneSide.Left)]
    [InlineData("thigh_R", BoneSide.Right)]
    [InlineData("L_clavicle", BoneSide.Left)]
    [InlineData("calf_r", BoneSide.Right)]
    [InlineData("mixamorig:LeftHand", BoneSide.Left)]
    [InlineData("RightUpLeg", BoneSide.Right)]
    [InlineData("heel.02.L", BoneSide.Left)]
    [InlineData("spine.003", BoneSide.Center)]
    [InlineData("Hips", BoneSide.Center)]
    [InlineData("Root", BoneSide.Center)]
    public void Side_comes_from_common_naming_schemes(string name, BoneSide side) =>
        Assert.Equal(side, BoneNames.SideOf(name));

    [Fact]
    public void Joints_get_spheres_and_parents_get_pyramids_in_maya_side_colors()
    {
        var (scene, skeleton) = SceneWithArm();
        var display = skeleton.DisplayNode!.OutputValues;

        // Two joints × three 16-segment circles, plus one pyramid (4 edges + 4 base lines) from Lower to Upper.
        Assert.Equal((2 * 3 * 16) + 8, display.Lines.Length);
        Assert.All(display.Lines.ToArray(), l => Assert.Equal(MayaBoneColors.Center, l.Color));
        Assert.True(display.JointRadius > 0f);
        Assert.NotNull(scene);
    }

    [Fact]
    public void Bone_size_scales_the_joint_spheres()
    {
        var (_, skeleton) = SceneWithArm();
        var radius = skeleton.DisplayNode!.OutputValues.JointRadius;

        skeleton.BoneSize = 2f;

        Assert.Equal(radius * 2f, skeleton.DisplayNode.OutputValues.JointRadius, 1e-5f);
    }

    [Fact]
    public void Selected_joint_and_its_bones_turn_maya_selection_green()
    {
        var (_, skeleton) = SceneWithArm();
        var changes = 0;
        skeleton.SelectedBoneChanged += (_, _) => changes++;

        skeleton.SelectedBone = 0;

        var lines = skeleton.DisplayNode!.OutputValues.Lines.ToArray();
        Assert.Equal((3 * 16) + 8, lines.Count(l => l.Color == MayaBoneColors.Selected)); // Lower's sphere and its pyramid
        Assert.Equal(1, changes);

        skeleton.SelectedBone = 99; // out of range clears
        Assert.True(skeleton.SelectedBone.IsNone);
    }

    [Fact]
    public void Uniform_mode_uses_maya_default_blue()
    {
        var (_, skeleton) = SceneWithArm();

        skeleton.ColorMode = BoneColorMode.Uniform;

        Assert.All(skeleton.DisplayNode!.OutputValues.Lines.ToArray(), l => Assert.Equal(MayaBoneColors.Default, l.Color));
    }

    [Fact]
    public void Clicking_a_joint_or_a_bone_picks_it()
    {
        var (scene, skeleton) = SceneWithArm();
        var size = new Vector2(400, 400);
        var camera = scene.Camera.CameraNode!.OutputValues;
        var viewProjection = camera.View * camera.Projection;
        var joints = skeleton.DisplayNode!.OutputValues.Joints.ToArray();
        var lower = BonePicker.ToScreen(joints[0], viewProjection, size)!.Value;
        var upper = BonePicker.ToScreen(joints[1], viewProjection, size)!.Value;

        Assert.Equal(1, BonePicker.Pick(scene, viewProjection, size, upper + new Vector2(2, 1)).Value.Bone);
        // Halfway along the bone picks the joint it starts from.
        var middle = BonePicker.Pick(scene, viewProjection, size, Vector2.Lerp(lower, upper, 0.5f)).Value;
        Assert.Same(skeleton, middle.Skeleton);
        Assert.Equal(0, middle.Bone);
        Assert.True(BonePicker.Pick(scene, viewProjection, size, new Vector2(5, 5)).IsNone);
    }

    [Fact]
    public void Hidden_bones_cannot_be_picked_and_show_bones_applies_to_new_meshes()
    {
        var (scene, skeleton) = SceneWithArm();

        scene.ShowBones = false;

        Assert.False(skeleton.Visible);
        var camera = scene.Camera.CameraNode!.OutputValues;
        Assert.True(BonePicker.Pick(scene, camera.View * camera.Projection, new Vector2(400, 400), new Vector2(200, 200)).IsNone);
    }

    private (SceneRoot Scene, SkeletonComponent Skeleton) SceneWithArm()
    {
        var root = Graph.Graph.CreateRoot();
        var scene = new SceneRoot(root);
        var project = new CharacterProject("Demo");
        var mesh = GltfImporter.Load(TestSkinnedMeshes.WriteBentArm(_temp.Combine("Arm.glb"), Vector3.Zero));
        var asset = project.Meshes.Add(new SkeletalMeshAsset("Arm") { Geometry = mesh.Geometry, Skeleton = mesh.Skeleton });
        scene.Show(project);
        scene.FrameAll();
        var skeleton = Assert.Single(scene.Skeletons);
        Assert.Equal(Maybe.Some(asset.Id), scene.FindMesh(asset.Id).Map(m => m.AssetId));
        return (scene, skeleton);
    }
}
