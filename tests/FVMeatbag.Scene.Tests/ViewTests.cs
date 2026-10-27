using System.Numerics;
using FVMeatbag.Core.Project;
using FVMeatbag.Scene.Components;

namespace FVMeatbag.Scene.Tests;

public class ViewTests
{
    [Fact]
    public void Every_scene_has_one_camera_and_one_light_that_cannot_be_removed()
    {
        var scene = new SceneRoot(Graph.Graph.CreateRoot());

        Assert.Single(scene.Root.Children.OfType<OrbitCameraComponent>());
        Assert.Single(scene.Root.Children.OfType<DirectionalLightComponent>());
        Assert.Throws<InvalidOperationException>(() => scene.Root.RemoveChild(scene.Camera));
        Assert.Throws<InvalidOperationException>(() => scene.Root.RemoveChild(scene.Light));
    }

    [Fact]
    public void Showing_a_project_keeps_the_camera_and_light_and_applies_its_settings()
    {
        var scene = new SceneRoot(Graph.Graph.CreateRoot());
        var camera = scene.Camera;
        var project = new CharacterProject("Hero") { Light = DirectionalLightSettings.Default with { Intensity = 4 } };
        project.Camera = project.Camera with { YawDegrees = 30 };

        scene.Show(project);

        Assert.Same(camera, scene.Camera);
        Assert.Equal(30, scene.Camera.Settings.YawDegrees);
        Assert.Equal(4 * Vector3.One, scene.Light.LightNode!.OutputValues.Radiance);
    }

    [Fact]
    public void Default_camera_looks_at_the_front_of_a_gltf_model()
    {
        var scene = new SceneRoot(Graph.Graph.CreateRoot());

        var output = scene.Camera.CameraNode!.OutputValues;

        Assert.True(output.Eye.Z > 0, "glTF models face +Z, so the camera starts on the +Z side.");
        var originInView = Vector3.Transform(Vector3.Zero, output.View);
        Assert.Equal(0, originInView.X, 1e-5f);
        Assert.True(originInView.Z < 0, "Right-handed view space looks down -Z.");
    }

    [Fact]
    public void Orbit_clamps_pitch_and_wraps_yaw()
    {
        var scene = new SceneRoot(Graph.Graph.CreateRoot());

        scene.Camera.Orbit(190, 200);

        Assert.Equal(-170, scene.Camera.Settings.YawDegrees, 1e-3f);
        Assert.Equal(OrbitCameraSettings.MaxPitch, scene.Camera.Settings.PitchDegrees);
    }

    [Fact]
    public void Pan_moves_the_target_across_the_view_plane_only()
    {
        var scene = new SceneRoot(Graph.Graph.CreateRoot());
        scene.Camera.Settings = OrbitCameraSettings.Default with { PitchDegrees = 0 };

        scene.Camera.Pan(100, 0, 500); // drag right: the scene follows, so the target moves left

        var target = scene.Camera.Settings.Target;
        Assert.True(target.X < 0);
        Assert.Equal(0, target.Y, 1e-5f);
        Assert.Equal(0, target.Z, 1e-5f);
        Assert.Equal(OrbitCameraSettings.Default.Distance, scene.Camera.Settings.Distance);
    }

    [Fact]
    public void Aspect_ratio_reaches_the_projection_through_the_graph()
    {
        var scene = new SceneRoot(Graph.Graph.CreateRoot());

        scene.Camera.AspectRatio = 2f;

        var projection = scene.Camera.CameraNode!.OutputValues.Projection;
        Assert.Equal(projection.M22 / 2f, projection.M11, 1e-5f);
    }
}
