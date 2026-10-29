# FV Meatbag Factory

A desktop character creator: create projects, load base skeletal meshes, sculpt, edit weights,
build morphs and poses, and round-trip edits with Unreal Engine 5.

Built in C# on .NET 10 with Avalonia (MVVM) for the UI and Vulkan (Silk.NET) for rendering.
Every feature is assembled from small, reusable nodes in a standalone graph runtime.

## Layout

| Project | What it is |
| --- | --- |
| `src/FVMeatbag.Graph` | Standalone node-graph runtime. No dependencies. |
| `src/FVMeatbag.Core` | Project hierarchy (the document model that owns meshes, morphs, poses). |
| `src/FVMeatbag.Scene` | Root scene and scene components (visual only), each owning a child graph. |
| `src/FVMeatbag.Rendering` | Backend-agnostic render contracts and the PBR material model. |
| `src/FVMeatbag.Rendering.Vulkan` | Vulkan backend on Silk.NET: offscreen clay-shaded rendering with MSAA. |
| `src/FVMeatbag.App` | Avalonia desktop shell (Windows first, Linux later). |
| `tests/*` | xUnit v3 tests for the graph runtime, scene, projects and import, and offscreen rendering. |

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design.

## Using it

On first start the app installs the saved Demo project ([assets/Samples/Demo](assets/Samples/Demo/README.md))
as `Documents/Projects/Demo`, with a sample character on the UE5 Manny skeleton (the CC0 3D Male Base Mesh, re-rigged), and opens it. Every project has one
camera and one light; select them in the outliner to adjust them.

- **File → Import Mesh** (Ctrl+I) loads a `.glb`/`.gltf` or `.fbx` into the open project and places it at
  the center of the scene. You can also pass a file on the command line:
  `dotnet run --project src/FVMeatbag.App -- Suzanne.glb`.
- **File → Save** (Ctrl+S) writes `project.json`; imported files are copied into the project's
  `Meshes` folder.
- Rigged glTF and FBX files keep their skeleton and skin weights. FBX files are converted to
  +Y up and meters; one saved in a pose imports in that pose. Bones are drawn Maya style: a wire
  sphere on each joint and a pyramid to each child, colored by side (left blue, right red,
  center yellow) or all in Maya's default blue (the **Bone colors** options "By side" and
  "Default"); the selected bone turns green.
- Click a joint or bone in the viewport, or a bone in the skeleton's tree in **Details**, to
  select it; the other view follows. Details also has the bone size and color settings.
- The viewport's top bar switches between wireframe, solid, solid + wireframe and shaded
  (keys 4 to 7, as in Maya; shaded is a placeholder that draws like solid until materials
  exist), shows or hides bones, and frames the scene.
- **Posing.** The tool overlay under the top bar picks select, move, rotate or scale (Q, W, E,
  R). With a bone selected, drag a gizmo handle: move arrows go along world axes and the
  square moves in the view plane; rotate rings turn about the bone's own axes and the outer
  ring about the view; scale handles stretch along the bone's axes and the center cube scales
  evenly. The mesh follows the bones (linear blend skinning). Details shows the bone's
  translate, rotate (relative to rest, like Maya's joint orient) and scale, with **Reset bone**
  and **Reset pose**. Poses are saved with the project.
- **Muscles.** Select **Muscles** under a skeleton in the outliner and press **Build from the
  body template**: the body's main muscles (the Flicker Void tools' template, 18 muscles in 29
  strands a side) are placed under the skin of a UE5 Manny-rigged mesh, mirrored to the right
  side and fitted to the skin. They are a see-through preview (opacity and X-ray in Details, the
  arm button in the top bar shows or hides them) that follows the pose: muscles shorten and
  bulge as joints bend, and broad ones like the gluteus also bend with nearby bones. Pick a
  muscle in the list to highlight it and change its width, thickness, belly, tendon and bulge,
  fit it to the skin, mirror it to the other side, or reset it. Each muscle drives one or more
  corrective roles with a weight, and many muscles may drive one role; Details lists the tagged
  corrective bones it moves. Only the corrective bones go to Unreal. Muscles are saved with the
  project.
- Viewport: click to select a bone, drag empty space to orbit, right drag or Shift+drag to pan,
  wheel to zoom, F to frame.

The viewport needs a GPU driver with Vulkan 1.2. Third-party assets are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Build

Requires the .NET 10 SDK.

```sh
dotnet build
dotnet test
dotnet run --project src/FVMeatbag.App
```
