using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Project;
using FVMeatbag.Scene.Components;

namespace FVMeatbag.Scene.Tests;

public sealed class PoseTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_new_pose_is_the_rest_pose_and_leaves_the_mesh_untouched()
    {
        var (asset, skeleton) = SceneWithArm();
        var pose = asset.Pose.Value;

        Assert.True(pose.IsRest);
        var world = pose.ComputeWorld();
        for (var i = 0; i < world.Length; i++)
        {
            Assert.Equal(pose.Skeleton.WorldRest[i], world[i]);
        }
        // At rest the skinning node passes the imported arrays through.
        Assert.Equal(asset.Geometry.Value.Positions, skeleton.SkinningNode!.OutputValues.Positions);
    }

    [Fact]
    public void Rotating_a_bone_deforms_the_vertices_it_owns()
    {
        var (asset, skeleton) = SceneWithArm();
        var weights = asset.Geometry.Value.Skin.Value.ToArray();
        var lower = Enumerable.Range(0, weights.Length).Where(i => weights[i].Bone(0) == 0).ToArray();
        var lowerBefore = lower.Select(i => Positions(skeleton)[i]).ToArray();

        // The imported arm is bent 90° at the elbow; turning the upper bone back straightens it.
        skeleton.RotateBone(1, skeleton.Pose[1], Vector3.UnitZ, -MathF.PI / 2);

        var positions = Positions(skeleton);
        Assert.Equal(lowerBefore, lower.Select(i => positions[i]).ToArray());
        Assert.All(positions, p => Assert.True(MathF.Abs(p.X) <= 0.1001f && p.Y <= 2.0001f, $"{p} is off the straight arm."));
        Assert.Equal(2f, positions.Max(p => p.Y), 1e-4f);
        Assert.False(asset.Pose.Value.IsRest);
        Assert.True(asset.Pose.Value.IsPosed(1));
    }

    [Fact]
    public void Posed_bones_move_their_joints_and_reset_puts_them_back()
    {
        var (asset, skeleton) = SceneWithArm();
        var joints = skeleton.DisplayNode!.OutputValues.Joints.ToArray();

        skeleton.MoveBone(0, skeleton.Pose[0], new Vector3(0.5f, 0, 0));

        var moved = skeleton.DisplayNode.OutputValues.Joints.ToArray();
        AssertNear(joints[0] + new Vector3(0.5f, 0, 0), moved[0]);
        AssertNear(joints[1] + new Vector3(0.5f, 0, 0), moved[1]); // the child follows

        asset.Pose.Value.Reset();

        Assert.Equal(joints, skeleton.DisplayNode.OutputValues.Joints.ToArray());
        Assert.Equal(asset.Geometry.Value.Positions, skeleton.SkinningNode!.OutputValues.Positions);
    }

    [Fact]
    public void Scaling_a_bone_stretches_along_its_own_axes()
    {
        var (_, skeleton) = SceneWithArm();
        var start = skeleton.Pose[1];

        skeleton.ScaleBone(1, start, new Vector3(1, 2, 1));

        Assert.Equal(start.Scale * new Vector3(1, 2, 1), skeleton.Pose[1].Scale);
        // The upper bone runs along its own Y axis, which the bent elbow turns to world -X: it doubles in length.
        Assert.Equal(-2f, Positions(skeleton).Min(p => p.X), 1e-4f);
        Assert.Equal(0.1f, Positions(skeleton).Max(p => p.X), 1e-4f);
    }

    [Theory]
    [InlineData(30, 0, 0)]
    [InlineData(0, 45, 0)]
    [InlineData(0, 0, -60)]
    [InlineData(10, 20, 30)]
    public void Euler_angles_read_back_in_xyz_order(float x, float y, float z)
    {
        var rotation = Quaternion.CreateFromRotationMatrix(
            Matrix4x4.CreateRotationX(float.DegreesToRadians(x)) * Matrix4x4.CreateRotationY(float.DegreesToRadians(y)) * Matrix4x4.CreateRotationZ(float.DegreesToRadians(z)));

        var euler = BoneTransform.EulerDegrees(rotation);

        AssertNear(new Vector3(x, y, z), euler, 1e-2f);
    }

    private static Vector3[] Positions(SkeletonComponent skeleton) => skeleton.SkinningNode!.OutputValues.Positions.ToArray();

    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"Expected {expected}, got {actual}.");

    private (SkeletalMeshAsset Asset, SkeletonComponent Skeleton) SceneWithArm()
    {
        var scene = new SceneRoot(Graph.Graph.CreateRoot());
        var project = new CharacterProject("Arm");
        var mesh = GltfImporter.Load(TestSkinnedMeshes.WriteBentArm(_temp.Combine("Arm.glb"), Vector3.Zero));
        var asset = project.Meshes.Add(new SkeletalMeshAsset("Arm") { Geometry = mesh.Geometry, Skeleton = mesh.Skeleton });
        scene.Show(project);
        return (asset, Assert.Single(scene.Skeletons));
    }
}
