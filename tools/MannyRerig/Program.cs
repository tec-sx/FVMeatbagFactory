using System.Numerics;
using FVMeatbag.Core.Import;
using FVMeatbag.Core.Rig;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

// Rebuilds the CC0 male base mesh (Blender metarig, 56 bones) on the UE5 Manny skeleton:
// Manny names and hierarchy, twist bones, metacarpals, and a sample set of corrective bones.
// Usage: dotnet run --project tools/MannyRerig -- <MaleBaseMesh.glb> <MannyBaseMesh.glb>
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: MannyRerig <source .glb with the Blender metarig> <output .glb>");
    return 1;
}

var source = GltfImporter.Load(args[0]);
var sourceSkeleton = source.Skeleton.OrThrow("The source has no skeleton.");
var geometry = source.Geometry;
var sourceSkin = geometry.Skin.OrThrow("The source has no skin weights.");
var uvs = ReadUvs(args[0], geometry.VertexCount);

Matrix4x4 SourceWorld(string name) => sourceSkeleton.WorldRest[sourceSkeleton.FindBone(name).OrThrow($"The source has no bone {name}.")];
Vector3 Head(string name) => SourceWorld(name).Translation;
Matrix4x4 RotationOf(Matrix4x4 m) =>
    Matrix4x4.Decompose(m, out _, out var rotation, out _) ? Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation)) : Matrix4x4.Identity;
Matrix4x4 At(Matrix4x4 rotationFrom, Vector3 position) => RotationOf(rotationFrom) * Matrix4x4.CreateTranslation(position);

var bones = new List<(string Name, string? Parent, Matrix4x4 World)>();
void Add(string name, string? parent, Matrix4x4 world) => bones.Add((name, parent, world));

// Source bones that become several Manny bones along their length: (target, start as a fraction of head → tail).
var splits = new Dictionary<string, (string Tail, (string Target, float Start)[] Segments)>();
// Source bones that map onto one Manny bone (or are merged into one).
var direct = new Dictionary<string, string>();

void Split(string sourceBone, string tail, string parent, params (string Target, float Start)[] segments)
{
    splits[sourceBone] = (tail, segments);
    var head = Head(sourceBone);
    var end = Head(tail);
    foreach (var (target, start) in segments)
    {
        // Twist bones hang off the main bone; spine and neck segments chain.
        var p = segments[0].Target.StartsWith("spine", StringComparison.Ordinal) || segments[0].Target.StartsWith("neck", StringComparison.Ordinal)
            ? (target == segments[0].Target ? parent : bones[^1].Name)
            : (target == segments[0].Target ? parent : segments[0].Target);
        Add(target, p, At(SourceWorld(sourceBone), Vector3.Lerp(head, end, start)));
    }
}

void Map(string sourceBone, string target, string parent)
{
    direct[sourceBone] = target;
    Add(target, parent, At(SourceWorld(sourceBone), Head(sourceBone)));
}

Add(MannySkeleton.Root, null, Matrix4x4.Identity);
Map("spine", "pelvis", "root");
Split("spine.001", "spine.002", "pelvis", ("spine_01", 0f), ("spine_02", 0.5f));
Split("spine.002", "spine.003", "spine_02", ("spine_03", 0f), ("spine_04", 0.5f));
Map("spine.003", "spine_05", "spine_04");
Split("spine.004", "spine.005", "spine_05", ("neck_01", 0f), ("neck_02", 0.5f));
Map("spine.005", "head", "neck_02");
direct["pelvis.L"] = "pelvis";
direct["pelvis.R"] = "pelvis";

foreach (var (b, s) in new[] { ("L", "_l"), ("R", "_r") })
{
    Map($"shoulder.{b}", "clavicle" + s, "spine_05");
    Split($"upper_arm.{b}", $"forearm.{b}", "clavicle" + s, ("upperarm" + s, 0f), ("upperarm_twist_01" + s, 0.25f), ("upperarm_twist_02" + s, 0.5f));
    Split($"forearm.{b}", $"hand.{b}", "upperarm" + s, ("lowerarm" + s, 0f), ("lowerarm_twist_02" + s, 0.5f), ("lowerarm_twist_01" + s, 0.75f));
    Map($"hand.{b}", "hand" + s, "lowerarm" + s);
    Map($"thumb.01.{b}", "thumb_01" + s, "hand" + s);
    Map($"thumb.02.{b}", "thumb_02" + s, "thumb_01" + s);
    Map($"thumb.03.{b}", "thumb_03" + s, "thumb_02" + s);
    foreach (var finger in new[] { "index", "middle", "ring", "pinky" })
    {
        // The metarig has no metacarpals: put one a third of the way from the wrist to the knuckle.
        // The palm stays weighted to the hand.
        var metacarpal = $"{finger}_metacarpal{s}";
        Add(metacarpal, "hand" + s, At(SourceWorld($"hand.{b}"), Vector3.Lerp(Head($"hand.{b}"), Head($"f_{finger}.01.{b}"), 1f / 3)));
        Map($"f_{finger}.01.{b}", $"{finger}_01{s}", metacarpal);
        Map($"f_{finger}.02.{b}", $"{finger}_02{s}", $"{finger}_01{s}");
        Map($"f_{finger}.03.{b}", $"{finger}_03{s}", $"{finger}_02{s}");
    }
}
foreach (var (b, s) in new[] { ("L", "_l"), ("R", "_r") })
{
    Split($"thigh.{b}", $"shin.{b}", "pelvis", ("thigh" + s, 0f), ("thigh_twist_01" + s, 0.25f), ("thigh_twist_02" + s, 0.5f));
    Split($"shin.{b}", $"foot.{b}", "thigh" + s, ("calf" + s, 0f), ("calf_twist_02" + s, 0.5f), ("calf_twist_01" + s, 0.75f));
    Map($"foot.{b}", "foot" + s, "calf" + s);
    Map($"toe.{b}", "ball" + s, "foot" + s);
    direct[$"heel.02.{b}"] = "foot" + s;
}

// Sample correctives, named like a typical game rig. They carry no weights yet: the corrective
// solve fits those. Each sits inside the body, under the muscle it stands for.
var positions = geometry.Positions.ToArray();
var up = Vector3.UnitY;
var front = Vector3.UnitX; // the character faces +X
var leftOut = Vector3.Normalize(new Vector3(0, 0, MathF.Sign(Head("shoulder.L").Z)));
Vector3 Target(string name) => bones.First(t => t.Name == name).World.Translation;

foreach (var (s, outward) in new[] { ("_l", leftOut), ("_r", -leftOut) })
{
    void Corrective(string name, string parent, Vector3 from, Vector3 to, float t, Vector3 direction)
    {
        var p0 = Vector3.Lerp(from, to, t);
        var axis = Vector3.Normalize(to - from);
        var d = Vector3.Normalize(direction - (Vector3.Dot(direction, axis) * axis));
        var depth = SurfaceDepth(positions, p0, axis, d);
        Add(name + s, parent, At(bones.First(b => b.Name == parent).World, p0 + (d * depth * 0.6f)));
    }
    var upperarm = Target("upperarm" + s);
    var lowerarm = Target("lowerarm" + s);
    var thigh = Target("thigh" + s);
    var calf = Target("calf" + s);
    var foot = Target("foot" + s);
    Corrective("shoulder", "upperarm" + s, upperarm, lowerarm, 0.12f, outward + (up * 0.5f));
    Corrective("bicep", "upperarm" + s, upperarm, lowerarm, 0.5f, front);
    Corrective("tricep", "upperarm" + s, upperarm, lowerarm, 0.5f, -front);
    Corrective("elbow", "lowerarm" + s, lowerarm, Target("hand" + s), 0.02f, -front);
    Corrective("trapezius", "spine_05", Target("neck_01"), upperarm, 0.35f, up - (front * 0.5f));
    Corrective("pectoralis", "spine_05", Target("spine_05"), upperarm, 0.45f, front);
    Corrective("latissimus", "spine_04", Target("spine_04"), upperarm, 0.45f, -front + (outward * 0.5f));
    Corrective("gluteus", "pelvis", thigh, calf, 0.05f, -front);
    Corrective("hip_flexor", "pelvis", thigh, calf, 0.08f, front);
    Corrective("quadriceps", "thigh" + s, thigh, calf, 0.45f, front);
    Corrective("knee", "calf" + s, calf, foot, 0.02f, front);
    Corrective("gastrocnemius", "calf" + s, calf, foot, 0.3f, -front);
    Corrective("achilles_tendon", "calf" + s, calf, foot, 0.85f, -front);
}

var index = bones.Select((b, i) => (b.Name, i)).ToDictionary(x => x.Name, x => x.i);

// Weights: direct bones carry over; split bones are shared along their length with smooth seams.
var weights = new SkinWeights[geometry.VertexCount];
for (var v = 0; v < weights.Length; v++)
{
    var influences = new List<(int Bone, float Weight)>();
    foreach (var (bone, weight) in sourceSkin.Span[v].Influences())
    {
        var name = sourceSkeleton.Bones[bone].Name;
        if (direct.TryGetValue(name, out var target))
        {
            influences.Add((index[target], weight));
        }
        else if (splits.TryGetValue(name, out var split))
        {
            var head = Head(name);
            var axis = Head(split.Tail) - head;
            var t = Vector3.Dot(positions[v] - head, axis) / axis.LengthSquared();
            for (var k = 0; k < split.Segments.Length; k++)
            {
                var enter = k == 0 ? 1f : Smooth(t - split.Segments[k].Start);
                var leave = k + 1 < split.Segments.Length ? Smooth(t - split.Segments[k + 1].Start) : 0f;
                if (enter - leave > 0f)
                {
                    influences.Add((index[split.Segments[k].Target], weight * (enter - leave)));
                }
            }
        }
        else
        {
            throw new InvalidOperationException($"No Manny bone for the source bone {name}.");
        }
    }
    weights[v] = SkinWeights.FromInfluences(influences);
}

// Write the glTF: vertices in the rest pose, inverse bind matrices from the new bones.
var nodes = new NodeBuilder[bones.Count];
for (var i = 0; i < bones.Count; i++)
{
    var (name, parent, world) = bones[i];
    if (parent is null)
    {
        nodes[i] = new NodeBuilder(name);
    }
    else
    {
        Matrix4x4.Invert(bones[index[parent]].World, out var parentInverse);
        nodes[i] = nodes[index[parent]].CreateNode(name);
        nodes[i].LocalMatrix = world * parentInverse;
    }
}

var mesh = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexJoints8>("MannyBaseMesh");
var primitive = mesh.UsePrimitive(MaterialBuilder.CreateDefault());
var normals = geometry.Normals.ToArray();
VertexBuilder<VertexPositionNormal, VertexTexture1, VertexJoints8> Vertex(uint i)
{
    var joints = new VertexJoints8([.. weights[i].Influences().Select(x => (x.Bone, x.Weight))]);
    return new(new VertexPositionNormal(positions[i], normals[(int)i]), new VertexTexture1(uvs[i]), joints);
}
var indices = geometry.Indices.Span;
for (var i = 0; i + 2 < indices.Length; i += 3)
{
    primitive.AddTriangle(Vertex(indices[i]), Vertex(indices[i + 1]), Vertex(indices[i + 2]));
}

var scene = new SceneBuilder();
scene.AddSkinnedMesh(mesh, [.. bones.Select((b, i) => (nodes[i], Matrix4x4.Invert(b.World, out var inverse) ? inverse : Matrix4x4.Identity))]);
var model = scene.ToGltf2();
model.Asset.Generator = "FVMeatbagFactory MannyRerig";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
model.SaveGLB(args[1]);

var check = MannyRig.Check(GltfImporter.Load(args[1]).Skeleton.OrThrow("The output has no skeleton."));
Console.WriteLine($"{args[1]}: {bones.Count} bones. {check.Summary}");
foreach (var problem in check.Problems)
{
    Console.WriteLine("  " + problem.Message);
}
return check.IsConformant ? 0 : 2;

// 0 well before a seam, 1 well after it, smooth over 8% of the bone length either side.
static float Smooth(float x)
{
    const float half = 0.08f;
    var t = Math.Clamp((x + half) / (2 * half), 0f, 1f);
    return t * t * (3 - (2 * t));
}

// How far the skin is from p0 along d, looking at vertices in a thin slice across the bone.
static float SurfaceDepth(Vector3[] positions, Vector3 p0, Vector3 axis, Vector3 d)
{
    var best = 0f;
    foreach (var p in positions)
    {
        var offset = p - p0;
        var along = Vector3.Dot(offset, axis);
        if (MathF.Abs(along) > 0.03f)
        {
            continue;
        }
        var across = offset - (along * axis);
        var distance = Vector3.Dot(across, d);
        if (distance > 0f && distance > across.Length() * 0.8f)
        {
            best = MathF.Max(best, distance);
        }
    }
    return best > 0f ? best : 0.05f;
}

// The importer keeps one vertex per source vertex, in order, for a single-primitive file.
static Vector2[] ReadUvs(string path, int vertexCount)
{
    var primitives = ModelRoot.Load(path).LogicalMeshes.SelectMany(m => m.Primitives).ToList();
    var uv = primitives.Count == 1 ? primitives[0].GetVertexAccessor("TEXCOORD_0")?.AsVector2Array() : null;
    return uv is not null && uv.Count == vertexCount ? [.. uv] : new Vector2[vertexCount];
}
