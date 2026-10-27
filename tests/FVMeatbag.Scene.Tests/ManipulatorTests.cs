using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Project;
using FVMeatbag.Scene.Components;
using FVMeatbag.Scene.Tools;

namespace FVMeatbag.Scene.Tests;

/// <summary>Drives the gizmos with pointer positions, as the viewport does.</summary>
public sealed class ManipulatorTests : IDisposable
{
    private static readonly Vector2 Size = new(400, 400);
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_select_tool_has_no_gizmo()
    {
        var (scene, skeleton) = SceneWithArm(selected: 0);
        var camera = CameraLookingAt(skeleton.BoneWorldMatrix(0).Translation);

        Assert.Empty(scene.Manipulator.Lines(camera));
        Assert.False(scene.Manipulator.BeginDrag(camera, Center(camera, skeleton, 0)));
    }

    [Fact]
    public void No_gizmo_without_a_selected_bone()
    {
        var (scene, _) = SceneWithArm(selected: null);
        scene.Manipulator.Tool = TransformTool.Move;

        Assert.Empty(scene.Manipulator.Lines(CameraLookingAt(Vector3.Zero)));
    }

    [Fact]
    public void Dragging_the_move_arrow_moves_the_bone_along_world_x_and_its_children_follow()
    {
        var (scene, skeleton) = SceneWithArm(selected: 0);
        scene.Manipulator.Tool = TransformTool.Move;
        var start = skeleton.BoneWorldMatrix(0).Translation;
        var childStart = skeleton.BoneWorldMatrix(1).Translation;
        var camera = CameraLookingAt(start);
        var center = Center(camera, skeleton, 0);

        Assert.Equal(GizmoHandle.X, scene.Manipulator.HitTest(camera, center + new Vector2(60, 0)));
        Assert.True(scene.Manipulator.BeginDrag(camera, center + new Vector2(60, 0)));
        scene.Manipulator.Drag(camera, center + new Vector2(110, 7)); // sideways wobble is ignored
        scene.Manipulator.EndDrag();

        var offset = new Vector3(50 * camera.WorldPerPixel(start), 0, 0);
        AssertNear(start + offset, skeleton.BoneWorldMatrix(0).Translation, 1e-3f);
        AssertNear(childStart + offset, skeleton.BoneWorldMatrix(1).Translation, 1e-3f);
    }

    [Fact]
    public void Dragging_the_move_square_moves_in_the_view_plane()
    {
        var (scene, skeleton) = SceneWithArm(selected: 0);
        scene.Manipulator.Tool = TransformTool.Move;
        var start = skeleton.BoneWorldMatrix(0).Translation;
        var camera = CameraLookingAt(start);
        var center = Center(camera, skeleton, 0);

        Assert.True(scene.Manipulator.BeginDrag(camera, center + new Vector2(2, 2)));
        scene.Manipulator.Drag(camera, center + new Vector2(2 + 20, 2 - 30));

        var perPixel = camera.WorldPerPixel(start);
        AssertNear(start + new Vector3(20 * perPixel, 30 * perPixel, 0), skeleton.BoneWorldMatrix(0).Translation, 1e-3f);
    }

    [Fact]
    public void Dragging_back_to_the_start_restores_the_bone()
    {
        var (scene, skeleton) = SceneWithArm(selected: 0);
        scene.Manipulator.Tool = TransformTool.Move;
        var before = skeleton.Pose[0];
        var camera = CameraLookingAt(skeleton.BoneWorldMatrix(0).Translation);
        var grab = Center(camera, skeleton, 0) + new Vector2(60, 0);

        scene.Manipulator.BeginDrag(camera, grab);
        scene.Manipulator.Drag(camera, grab + new Vector2(80, 0));
        scene.Manipulator.Drag(camera, grab);
        scene.Manipulator.EndDrag();

        Assert.Equal(before, skeleton.Pose[0]);
        Assert.True(skeleton.Pose.IsRest);
    }

    [Fact]
    public void Dragging_a_rotate_ring_turns_the_bone_about_that_axis_in_the_dragged_direction()
    {
        // The upper bone is turned 90° about Z, so its local Z is world Z and its ring faces the camera.
        var (scene, skeleton) = SceneWithArm(selected: 1);
        scene.Manipulator.Tool = TransformTool.Rotate;
        var world = skeleton.BoneWorldMatrix(1);
        var camera = CameraLookingAt(world.Translation);
        var center = Center(camera, skeleton, 1);
        var radius = 0.8f * BoneManipulator.SizePixels;
        Vector2 OnRing(float degrees) => center + (radius * new Vector2(MathF.Cos(float.DegreesToRadians(degrees)), -MathF.Sin(float.DegreesToRadians(degrees))));

        Assert.Equal(GizmoHandle.Z, scene.Manipulator.HitTest(camera, OnRing(45)));
        Assert.True(scene.Manipulator.BeginDrag(camera, OnRing(45)));
        scene.Manipulator.Drag(camera, OnRing(75)); // counterclockwise on screen

        var after = skeleton.BoneWorldMatrix(1);
        AssertNear(world.Translation, after.Translation); // turns about its own origin
        var turned = MathF.Atan2(after.M12, after.M11) - MathF.Atan2(world.M12, world.M11);
        // The tangent drag of a 30° chord is a little shorter than the arc.
        Assert.InRange(float.RadiansToDegrees(turned), 26f, 31f);
        Assert.Equal(world.M33, after.M33, 1e-4f); // still about Z only
    }

    [Fact]
    public void Dragging_a_scale_handle_scales_that_axis_only()
    {
        // The upper bone's local X points up the screen.
        var (scene, skeleton) = SceneWithArm(selected: 1);
        scene.Manipulator.Tool = TransformTool.Scale;
        var start = skeleton.Pose[1];
        var camera = CameraLookingAt(skeleton.BoneWorldMatrix(1).Translation);
        var center = Center(camera, skeleton, 1);

        Assert.Equal(GizmoHandle.X, scene.Manipulator.HitTest(camera, center + new Vector2(0, -60)));
        scene.Manipulator.BeginDrag(camera, center + new Vector2(0, -60));
        scene.Manipulator.Drag(camera, center + new Vector2(0, -110));

        var scale = skeleton.Pose[1].Scale;
        Assert.Equal(start.Scale.X * 1.5f, scale.X, 1e-3f);
        Assert.Equal(start.Scale.Y, scale.Y);
        Assert.Equal(start.Scale.Z, scale.Z);
    }

    [Fact]
    public void The_hovered_handle_is_drawn_in_yellow()
    {
        var (scene, skeleton) = SceneWithArm(selected: 0);
        scene.Manipulator.Tool = TransformTool.Move;
        var camera = CameraLookingAt(skeleton.BoneWorldMatrix(0).Translation);

        Assert.True(scene.Manipulator.UpdateHover(camera, Center(camera, skeleton, 0) + new Vector2(60, 0)));

        Assert.Equal(GizmoHandle.X, scene.Manipulator.Hovered);
        Assert.Contains(scene.Manipulator.Lines(camera), l => l.Color == BoneManipulator.ActiveColor);
        Assert.DoesNotContain(scene.Manipulator.Lines(camera), l => l.Color == BoneManipulator.XColor);
    }

    private static Vector2 Center(ViewportCamera camera, SkeletonComponent skeleton, int bone) =>
        camera.ToScreen(skeleton.BoneWorldMatrix(bone).Translation)!.Value;

    /// <summary>A camera 5 units in front (+Z) of <paramref name="target"/>, looking at it.</summary>
    private static ViewportCamera CameraLookingAt(Vector3 target)
    {
        var eye = target + new Vector3(0, 0, 5);
        return new ViewportCamera(
            Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY),
            Matrix4x4.CreatePerspectiveFieldOfView(float.DegreesToRadians(40), 1f, 0.01f, 100f),
            eye,
            Size);
    }

    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"Expected {expected}, got {actual}.");

    private (SceneRoot Scene, SkeletonComponent Skeleton) SceneWithArm(int? selected)
    {
        var scene = new SceneRoot(Graph.Graph.CreateRoot());
        var project = new CharacterProject("Arm");
        var mesh = GltfImporter.Load(TestSkinnedMeshes.WriteBentArm(_temp.Combine("Arm.glb"), Vector3.Zero));
        project.Meshes.Add(new SkeletalMeshAsset("Arm") { Geometry = mesh.Geometry, Skeleton = mesh.Skeleton });
        scene.Show(project);
        var skeleton = Assert.Single(scene.Skeletons);
        if (selected is { } bone)
        {
            skeleton.SelectedBone = bone;
        }
        return (scene, skeleton);
    }
}
