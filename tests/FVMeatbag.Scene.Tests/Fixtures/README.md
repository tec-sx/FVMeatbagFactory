# Test fixtures

FBX exports of the Demo character (`assets/Samples/Demo/Meshes/MaleBaseMesh.glb`, CC0, see
`MaleBaseMesh.LICENSE.txt` next to it), used to check the FBX importer against the glTF one.

| File | Blender export settings |
| --- | --- |
| `MaleBaseMesh.blender-defaults.fbx` | Defaults: Y up, scale stored on the nodes (100) in centimeter units, `_end` leaf bones added |
| `MaleBaseMesh.zup-scale-all.fbx` | Z up, "FBX All" unit scale (stored in the file's UnitScaleFactor), no leaf bones |
| `MaleBaseMesh.posed-in-moved-rig.fbx` | Defaults without leaf bones; the mesh is parented to an armature moved to (0.5, 0, 0.2) and saved posed (left forearm bent 60°, spine raised 10 cm) |

All were made with Blender 5.0.1 by `export_fbx.py`, which also prints what Blender shows for the posed file:

```
blender --background --python export_fbx.py -- path/to/MaleBaseMesh.glb path/to/Fixtures
```

The script removes the hidden "Icosphere" bone-shape object that Blender's glTF importer adds.
