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

Threading: single-threaded by design (the UI thread). Long work (import, bake) will become async
operations that complete into a later pass. **Open:** async node API.

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

Your "load mesh → create project → add to scene" flow fits as: a *Load mesh* node outputs mesh
data; a *Create project* node writes it into the hierarchy; an *Add to scene* node creates the
`MeshComponent`. **Open (main decision):** these steps are actions with side effects rather than
pure value transforms. Options:

1. *Data-only graph* (what exists now): side-effect nodes react to a changed value, e.g. a
   "request id" counter. Simple, but ordering of actions is implicit.
2. *Data + execution pins* (like UE Blueprints): explicit "then" wires for actions, data wires for
   values. Clearer for workflows and gizmo state machines, more runtime work.

Recommendation: add execution/event pins as a second edge kind before building the gizmo and the
load/create/add flow; keep data propagation exactly as it is.

Undo/redo should live at the graph-edit level (input changes, connect, add/remove) as commands.
**Open.**

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
UE left-handed Z-up centimeters. **Open:** pick the internal convention (recommendation: glTF's,
converting only at the import/export and UE bridge boundaries).

Skin will eventually want subsurface scattering (UE Subsurface Profile / Substrate); that is an
extension on top of the base model, not a replacement.

## 5. Unreal bridge (later)

Two directions: push edits to UE (skeletal mesh, morph targets, poses) and read edits made in UE
back. Likely an editor plugin on the UE side talking over a local socket, exchanging glTF or FBX
payloads plus small JSON commands. Out of scope for the groundwork.
