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
| `src/FVMeatbag.Rendering.Vulkan` | Vulkan backend on Silk.NET (instance creation only so far). |
| `src/FVMeatbag.App` | Avalonia desktop shell (Windows first, Linux later). |
| `tests/*` | xUnit v3 tests for the graph runtime and scene binding. |

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design.

## Build

Requires the .NET 10 SDK.

```sh
dotnet build
dotnet test
dotnet run --project src/FVMeatbag.App
```
