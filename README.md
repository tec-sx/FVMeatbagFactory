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

On first start the app creates `Documents/Projects/Demo` with a rigged sample character
([3D Male Base Mesh](assets/Samples/MaleBaseMesh/README.md), CC0) and opens it. Every project has one
camera and one light; select them in the outliner to adjust them.

- **File → Import Mesh** (Ctrl+I) loads a `.glb`/`.gltf` into the open project and places it at
  the center of the scene. You can also pass a file on the command line:
  `dotnet run --project src/FVMeatbag.App -- Suzanne.glb`.
- **File → Save** (Ctrl+S) writes `project.json`; imported files are copied into the project's
  `Meshes` folder.
- Rigged glTF files keep their skeleton and skin weights; select a mesh's **Skeleton** in the
  outliner to show or hide its bones.
- Viewport: drag to orbit, right drag or Shift+drag to pan, wheel to zoom, F to frame.

The viewport needs a GPU driver with Vulkan 1.2.

## Build

Requires the .NET 10 SDK.

```sh
dotnet build
dotnet test
dotnet run --project src/FVMeatbag.App
```
