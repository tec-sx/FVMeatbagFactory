# Demo project

A saved project that the app installs as `Documents/Projects/Demo` on first run. It holds the
sample character below, with the camera at its front.

## Manny base mesh (`Meshes/MannyBaseMesh.glb`)

A low-poly male base mesh in a T-pose, rigged to the UE5 Mannequin ("Manny") skeleton: Manny's
bone names and hierarchy (80 core bones, with twist bones and metacarpals) plus 26 sample
corrective bones named like a game rig (`bicep_l`, `hip_flexor_r`, `achilles_tendon_l`, ...).
No materials, textures or animations. It faces +X (glTF convention is +Z), so the Demo camera
starts at yaw 90°.

- Made by `tools/MannyRerig` from the CC0 "3D Male Base Mesh" (kept with its original metarig as
  `tests/FVMeatbag.Scene.Tests/Fixtures/MaleBaseMesh.glb`):

  ```
  dotnet run --project tools/MannyRerig -- tests/FVMeatbag.Scene.Tests/Fixtures/MaleBaseMesh.glb assets/Samples/Demo/Meshes/MannyBaseMesh.glb
  ```

- Original author: orange-juice-games (https://orange-juice-games.itch.io), packaged by BoQsc
- Source: https://github.com/BoQsc/Godot-Male-Base-Mesh, latest release `male_base_mesh.zip` as of 2026-10-27
  (exported with Blender glTF I/O 4.3.47)
- License: CC0 1.0 Universal (public domain dedication), see [Meshes/MannyBaseMesh.LICENSE.txt](Meshes/MannyBaseMesh.LICENSE.txt)
- Changes: re-rigged to the Manny hierarchy. Bone orientations follow the original metarig (Y
  along the bone), not Manny's, and the corrective bones carry no weights yet: the corrective
  solve fits them.
