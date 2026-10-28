# Architecture

This is the groundwork design. Sections marked **Open** are decisions still to be made.

## 1. Layers

```
FVMeatbag.App (Avalonia, MVVM)
   ├── FVMeatbag.Scene ──────┐
   │      └── FVMeatbag.Core │   (project hierarchy)
   ├── FVMeatbag.Rendering.Vulkan ── FVMeatbag.Rendering
   └── FVMeatbag.Graph  ◄────┘   (standalone, no dependencies)
```

`FVMeatbag.Graph` must never reference anything above it. Feature code (import, sculpt, weights,
morphs, poses, UE bridge) is written as operations and plugged into graphs by the layers above.

## 2. Node graph runtime

### Node = input struct + output struct + operation

```csharp
public struct BrushInput  { public float Radius; public float Strength; public MeshHandle Mesh; }
public struct BrushOutput { public MeshHandle Mesh; }

public sealed class BrushOperation : Operation<BrushInput, BrushOutput>
{
    public override void Execute(in BrushInput input, ref BrushOutput output, EvaluationContext context) { ... }
}

var brush = graph.AddNode(new BrushOperation());
brush.UpdateInputs((ref BrushInput i) => i.Radius = 2f);
```

- Every public field of the input/output struct is a port (`[Port("Display name")]` renames,
  `[PortIgnore]` excludes). The schema is built once per struct type with compiled by-ref
  accessors, so reading or writing a port never boxes. A source generator can replace the
  reflection later for NativeAOT without API changes.
- `Execute` receives the previous output by `ref`, so operations can keep caches and only
  overwrite what changed.
- Edges connect an output port to an input port of exactly the same type. Conversions are nodes.
  An input has at most one incoming edge; an output fans out freely.

### Optional values: `Maybe<T>`

Graphs are full of values that are legitimately absent: no mesh loaded yet, no project, nothing
selected. Ports and lookups model that with `Maybe<T>` instead of nulls:

- `default(Maybe<T>)` is None, so a fresh port struct starts as "nothing yet".
- Equality compares presence, then the value, so None → None does not trigger downstream work.
- `Map`, `Bind`, `Where`, `Match`, `OrDefault` and `OrThrow` keep operations short. In actions,
  `input.Mesh.OrThrow("No mesh loaded.")` faults the node and stops the chain when data is missing.
- Lookups that may miss return it: `node.FindInput(name)`, `project.Find(id)`. The throwing
  variants (`node.Input(name)`) stay for wiring code where a miss is a programming error.

### Change propagation

You described each node listening to events on its inputs and recalculating. The runtime keeps
those semantics but routes them through a scheduler, because a naive event chain breaks on
diamond shapes (A feeds B and C, both feed D): D runs twice, once with a stale input, and deep
chains recurse on the stack.

1. Setting an input that actually changed marks the node **dirty** and queues it.
2. `GraphEvaluator.Evaluate()` drains the queue in **topological rank** order (rank = 1 + the
   highest upstream rank, recomputed lazily when connections change). A node runs once per pass,
   only after everything upstream has settled.
3. Before running, a node pulls values across its incoming edges.
4. After running, outputs are compared field by field. **Only changed outputs** dirty downstream
   nodes, so propagation stops as soon as a result comes out the same.
5. `Node.OutputsChanged` and `GraphEvaluator.NodeEvaluated` are the events the outside world
   (view models, renderer) listens to.

Modes and batching:

- `EvaluationMode.Deferred` (the app): edits only queue work; the app evaluates once per frame.
  A sculpt stroke that changes inputs 200 times in a frame costs one downstream evaluation.
- `EvaluationMode.Immediate` (tests, tools): evaluates after every edit outside a batch.
- `using (evaluator.BeginBatch()) { ... }` groups edits and evaluates once at the end.

Cycles are rejected at connect time (`GraphCycleException`). Feedback, when needed, goes through
an explicit state/delay node that feeds its value into the *next* pass.

Failures: an operation that throws leaves the node `Faulted` with its previous outputs, nothing
downstream runs, and `NodeFaulted` is raised. The next input change retries.

Heavy data (meshes, textures) travels as references inside the structs. Change detection uses
`EqualityComparer<T>.Default`, so these should be immutable snapshots or versioned handles
(`SkeletalMeshAsset.Version` sketches this), not buffers mutated behind the graph's back.

Threading: single-threaded by design (the UI thread). Long work (a solve, a bake, an import) is an
`AsyncOperation<TIn, TOut>`:

- When the node evaluates, `RunAsync` starts on the thread pool with a copy of the inputs and the
  node is `Running`. Its outputs keep their previous values and nothing downstream runs yet.
- The result is queued and applied at the start of the next `Evaluate()`, like a normal run:
  changed outputs propagate, a throw faults the node. The app's frame tick picks it up because
  `HasPendingWork` includes finished work; `AsyncWorkCompleted` (raised off-thread) is there for
  hosts without a tick.
- An input change while running cancels and restarts the work; removing the node or
  `node.Cancel()` cancels it. A superseded result is dropped. `node.Progress` reads what
  `AsyncContext.ReportProgress` last reported.
- `AsyncActionOperation` is the exec-pin flavour: the chain pauses at the node and continues
  through `Then` (or whatever `AsyncContext.Fire` chose) once the work is done, so
  "sample poses → simulate → solve → export" is one chain.
- `evaluator.SettleAsync()` waits and evaluates until nothing is running, for tests and tools.

### Root graph, child graphs, subgraphs

- One **root graph** per application (`AppHost.RootGraph`). All child graphs share its evaluator,
  so nodes in different child graphs can connect and still evaluate in one global order.
- **Child graphs** are about ownership and lifetime. Removing a child graph removes its nodes,
  its edges (including edges into other graphs) and its descendants in one step.
- **Subgraphs** (a reusable graph wrapped as a single node with boundary input/output nodes) are
  the next step. **Open:** subgraph ports are dynamic, so they need a non-struct port path.

## 3. Project hierarchy and scene

```
Root graph "App"
└── Scene graph                    ← SceneRoot
    └── Root component graph       ← SceneComponent "Root"
        ├── Body graph             ← MeshComponent (reads asset from project hierarchy)
        │   └── Head graph
        └── Gizmo(Body) graph      ← GizmoComponent, wired to Body's world matrix
```

- The **project hierarchy** (`FVMeatbag.Core`) owns the data: meshes, skeletons, morphs, poses.
- **Scene components** are visual only. Each attached component owns a child graph that mirrors
  the component tree, and every component has a `WorldTransform` node chained to its parent's.
  A `MeshComponent` only holds the asset id; nodes in its graph read the asset from the hierarchy.
- **Selection** adds a `GizmoComponent` under the scene root whose world matrix is wired to the
  target's world output. Moving the target moves the gizmo through the graph. Move/rotate/scale
  interaction will be nodes in the gizmo's graph. Changing selection removes the gizmo graph.

### Camera and light

Every scene has exactly one `OrbitCameraComponent` and one `DirectionalLightComponent`, created by
`SceneRoot`. The app is about the character, not staging, so they are adjusted but never added or
removed (`SceneComponent.IsRemovable` is false and their constructors are internal). Each owns a
node in its graph that turns settings into what the renderer needs (view/projection matrices,
direction to the light and radiance), so the viewport redraws through the same change propagation
as everything else. Their settings belong to the project and are saved with it.

The camera orbits a target point (yaw, pitch, distance) and pans by moving that point across the
view plane. Viewport controls: a click selects a bone, left drag on a gizmo handle poses the
selected bone, left or middle drag elsewhere orbits, right drag or Shift+drag pans, the wheel
zooms, F frames the scene; Q, W, E, R pick the select, move, rotate and scale tools.

### Projects on disk

```
Documents/Projects/            ← ProjectWorkspace, created on first run like an installer would
└── Demo/                      ← installed on first run from assets/Samples/Demo and opened at startup
    ├── project.json           ← name, camera, light, mesh list (paths relative to the folder), poses
    └── Meshes/
        └── MannyBaseMesh.glb  ← the sample character (CC0); imported files are copied here too,
                                 so the folder is self-contained
```

The Demo is a saved project in the repository (`assets/Samples/Demo`) that the first run copies
in. An empty Demo left by an earlier build is filled from it too; any other existing Demo is
left alone. `ProjectFile` saves and opens this layout. A mesh whose file is missing stays in the project with
no geometry and is reported instead of failing the whole project. New projects get their own
folder next to Demo.

### Data wires and execution wires

The graph has two kinds of wire (decided 2026-10-27):

- **Data wires** carry values and propagate changes as described in section 2.
- **Exec wires** carry no value; they say "run this action next", like Blueprint's white pins.

An action is an `ActionOperation<TIn, TOut>`. Its node gets exec inputs (default `Execute`) and
exec outputs (default `Then`) and runs **only** when an exec chain reaches it, never because a
data input changed. Before each step the data graph is settled, so an action always sees current
inputs, including results of earlier steps. Its changed data outputs then propagate normally.

- `context.Fire("True")` picks which exec outputs continue (branches, sequences). If an action
  fires nothing and has exactly one exec output, that output fires automatically.
- Chains run depth first: everything a step fires finishes before the next queued trigger.
- A throwing action faults and stops its chain.
- `node.Trigger()` starts a chain from code or UI; in deferred mode it runs on the next frame,
  after the data graph has settled.
- An exec output leads to one input; an input accepts many. Exec loops are allowed (state
  machines) and runaway loops are stopped by `MaxEvaluationsPerPass`.

Two chains back the File menu, built from the same action nodes:

```
New Project from Mesh (NewCharacterWorkflow)
[Load mesh] ─▶ [Create project] ─▶ [Add mesh to project] ─▶ [Add to scene] ─▶ [Save project]

Import Mesh (ImportMeshWorkflow)
[Load mesh] ─▶ [Add mesh to project] ─▶ [Add to scene]
```

Load mesh picks the reader by extension (`MeshImporter`): glTF 2.0 (.glb/.gltf) with SharpGLTF,
FBX with the native Assimp library (Silk.NET.Assimp). Either way it bakes every mesh in the file into one
`MeshGeometry` (positions, normals, indices) in world space. Skinned meshes are deformed into the
rest pose the file shows (joint world × inverse bind, so armature transforms are honored); all
their joints become one `Skeleton` (bones ordered parents first, rest transforms, inverse bind =
inverse rest) and each vertex keeps up to eight normalized `SkinWeights` (what UE5 imports by default). Because the stored mesh
is already in the rest pose, bind pose = rest pose, which keeps later posing and weight editing
simple. A rigged mesh gets a `SkeletonComponent` child in the scene. Its `BoneDisplayOperation`
node turns the bones into Maya-style overlay lines that follow the mesh's transform: a
three-circle wire sphere per joint (radius from the skeleton's extent × bone size) and a
four-sided pyramid to each child, colored by side from the bone name (`.L`/`_L`/`L_`/"left",
and the same for right) or uniformly, with the selected bone in Maya's selection green. The
component holds the bone selection; `BonePicker` maps a viewport click to a joint (projected
sphere plus a few pixels of slack) or, failing that, to the bone segment's parent joint.

FBX specifics (`FbxImporter`): Assimp applies the file's axis system but not its units, so the
scene is scaled to meters from the UnitScaleFactor metadata (centimeters per unit; Blender's
default export stores 1 and puts a ×100 on the objects instead, "FBX All" stores 100). FBX does
not mark joints, so the skeleton is every bone that weights a vertex, every node between two of
them, and every mesh-free node below one (end joints such as Blender's `_end` leaf bones).
Blender writes bind matrices without the mesh object's transform, which Assimp does not expect,
so the importer tries the mesh's own space, its local transform and its transform below the
root, and keeps the one that leaves the skinned mesh where its node puts it. The Blender exports
of the Demo character in `tests/FVMeatbag.Scene.Tests/Fixtures` (default, Z-up with "FBX All"
scale, and a posed rig with the mesh parented to a moved armature) all import as Blender shows them.

Posing: the pose is document data, a `SkeletonPose` on the mesh asset (each bone's translation,
rotation and scale relative to its parent, starting at rest), saved in `project.json` for posed
bones only. The `SkeletonComponent` graph is pose → bone display and pose → skinning:
`PoseOperation` turns the pose (compared by version) into model-space bone transforms and
skinning matrices, and `SkinningOperation` deforms the mesh on the CPU with linear blend
skinning (passing the imported arrays through at rest); the viewport draws its output. The
`BoneManipulator` on the scene holds the current tool (select, move, rotate, scale) and the
gizmo for the selected bone: it draws the handles as overlay lines at a fixed size on screen,
hit-tests them in screen space and turns drags into pose edits relative to the bone's
transform when the drag began. Move works along world axes (closest point between the pointer
ray and the axis) or in the view plane, rotate about the bone's own axes or the view axis (drag
along the ring's screen tangent), scale along the bone's own axes or evenly.

Add mesh to project copies the source file into the project's
`Meshes` folder. Add to scene places the mesh so its bounds are centered on the origin and frames
the camera on it.

Undo/redo should live at the graph-edit level (input changes, connect, add/remove) as commands.
**Open.**

### Rig contract: UE5 Manny and corrective roles

The muscle and corrective system (design: the "Muscle and corrective bone system" proposal) relies
on bone names, so skeletons follow a contract (`FVMeatbag.Core.Rig`):

- **Core bones** are the UE5 Manny body (`MannySkeleton`): names and hierarchy, compared ignoring
  case like UE. Twist, IK, `interaction` and `center_of_mass` bones are optional, but must sit
  under the right parent when present. `MannyRig.Check` lists every problem (missing bone, wrong
  parent, duplicate name, second root) or returns a `MannyRig`; the `MannyCheckOperation` node in
  each skeleton's graph re-checks after renames, and downstream nodes take its `Maybe<MannyRig>`.
- **Extras** are every other bone: correctives, face bones, sockets. They may hang anywhere below
  the core, never above a core bone. `DriverOf` gives the core bone an extra follows.
- **Corrective roles** (`CorrectiveRoleCatalog`, from the embedded `CorrectiveRoles.json`) say
  what an extra stands for (biceps, knee, Achilles tendon), whatever it is called. A
  `CorrectiveTag` is role + side + optional part (`bicep_hi_l`). Names suggest tags
  (`bicep_l`, `Quad_Upper_R`, `hipFlexor_02_L`), the user can retag, mirror to the other side or
  rename the bone, and each extra has a mode: Solve, Lock (keep its weights) or Ignore. The
  `CorrectiveSetup` lives on the mesh asset and is saved in `project.json` with bone renames
  (keyed by the imported name).
- The Demo character is the CC0 base mesh re-rigged by `tools/MannyRerig` to the Manny hierarchy,
  with 26 sample correctives that carry no weights yet. User meshes stay out of the repository
  (`assets/Private/` is git-ignored and CI fails on mesh files outside samples and fixtures).

## 4. Rendering

### Silk.NET vs Vortice

Silk.NET. Both bind Vulkan well, but Silk.NET also covers what this app needs around it: a
cross-platform loader, windowing/input if ever needed, Assimp bindings for import, SPIR-V
reflection and shaderc. It is the larger and more active community. Vortice.Vulkan is lean and
fine, but you would assemble the rest yourself.

### Getting Vulkan into Avalonia

The backend renders to an **offscreen image**; the viewport control presents it.

1. Start: copy the rendered image back to a `WriteableBitmap`. Slow but simple and portable, good
   enough to bring the pipeline up.
2. Then: share the image with Avalonia's compositor via `ICompositionGpuInterop` (external memory
   handles on Windows and Linux) for zero-copy presentation.

A native child window with its own swapchain is the alternative, but popups and overlays cannot
draw over it ("airspace"), which hurts an editor viewport.

### PBR compatible with UE5, Substance Painter and Blender

Use the **glTF 2.0 metallic-roughness** model as the canonical material (`PbrMaterial`), since it
is the common subset of all three:

| Concern | Canonical choice | Notes |
| --- | --- | --- |
| Workflow | Metallic / roughness | UE5 default lit, Painter presets, Principled BSDF |
| Base color | sRGB texture × linear factor | |
| ORM | Packed R=AO, G=Roughness, B=Metallic, linear | Matches UE and glTF packing |
| Normal maps | OpenGL (+Y), MikkTSpace tangents | Flip green when exchanging with UE (DirectX) |
| Shading | Lambert diffuse + GGX / Smith height-correlated / Schlick | Same family UE uses |
| IBL | Split-sum: prefiltered radiance + irradiance + BRDF LUT | |
| Output | Linear HDR → filmic/ACES tonemap | Match UE's default for look parity |

Coordinate systems differ: glTF is right-handed Y-up meters, Blender right-handed Z-up meters,
UE left-handed Z-up centimeters. The internal convention is glTF's (decided with the first demo
scene); conversions happen only at the import/export and UE bridge boundaries. Blender's glTF
exporter already converts to +Y up, and models face +Z, which is where the default camera sits.

### Current renderer

The first renderer draws untextured "clay": per-vertex normals, one directional light (Lambert)
and a sky/ground hemisphere ambient, no materials. It renders offscreen with 4x MSAA where
supported, resolves into a BGRA8 image and copies it into a host-visible buffer that the
viewport control copies into a `WriteableBitmap` (step 1 above). Frames are only rendered when
the graph changed or the viewport resized. Back faces are lit too, since imported meshes are not
always closed. The display modes (wireframe, solid, solid + wireframe, shaded) are a
`FrameContext` field: wireframe redraws the meshes with polygon mode Line, depth tested
against the solid pass but not written (so wireframe-only shows hidden edges) with a small depth
bias; it needs the `fillModeNonSolid` device feature and falls back to solid without it. Shaded
draws like solid until materials exist. Overlay lines (bones) are drawn last without depth testing. Shaders live in `src/FVMeatbag.Rendering.Vulkan/Shaders` as GLSL with committed
SPIR-V. Offscreen tests render real frames on any Vulkan driver, including Mesa's software
lavapipe, and skip when there is none.

Skin will eventually want subsurface scattering (UE Subsurface Profile / Substrate); that is an
extension on top of the base model, not a replacement.

## 5. Unreal bridge (later)

Two directions: push edits to UE (skeletal mesh, morph targets, poses) and read edits made in UE
back. Likely an editor plugin on the UE side talking over a local socket, exchanging glTF or FBX
payloads plus small JSON commands. Out of scope for the groundwork.
