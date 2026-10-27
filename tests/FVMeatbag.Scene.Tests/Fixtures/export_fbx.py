# Writes the FBX fixtures from the Demo character. Run with Blender (5.0.1 was used):
#   blender --background --python export_fbx.py -- path/to/MaleBaseMesh.glb path/to/Fixtures
import sys, math, bpy

src, out = sys.argv[sys.argv.index('--') + 1:][:2]


def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    # Blender's glTF importer adds a hidden bone-shape object; it is not part of the character.
    for o in [o for o in bpy.data.objects if o.name.startswith('Icosphere')]:
        bpy.data.objects.remove(o, do_unlink=True)
    return bpy.data.objects['metarig'], bpy.data.objects['mesh']


load()
bpy.ops.export_scene.fbx(filepath=f"{out}/MaleBaseMesh.blender-defaults.fbx", bake_anim=False)

load()
bpy.ops.export_scene.fbx(filepath=f"{out}/MaleBaseMesh.zup-scale-all.fbx", axis_up='Z', axis_forward='Y',
                         apply_scale_options='FBX_SCALE_ALL', add_leaf_bones=False, bake_anim=False)

# Mesh parented to a moved armature, saved posed: the left forearm bent 60 degrees, the spine raised 10 cm.
rig, mesh = load()
rig.location = (0.5, 0, 0.2)
bpy.context.view_layer.update()
world = mesh.matrix_world.copy()
mesh.parent = rig
mesh.matrix_world = world
forearm = rig.pose.bones['forearm.L']
forearm.rotation_mode = 'XYZ'
forearm.rotation_euler = (0, 0, math.radians(60))
rig.pose.bones['spine'].location = (0, 0.1, 0)
bpy.context.view_layer.update()
bpy.ops.export_scene.fbx(filepath=f"{out}/MaleBaseMesh.posed-in-moved-rig.fbx", add_leaf_bones=False, bake_anim=False)

# What Blender shows for the posed file, in Y-up meters, for the test to compare against.
evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
points = [evaluated.matrix_world @ v.co for v in evaluated.to_mesh().vertices]
low = [min(p[i] for p in points) for i in range(3)]
high = [max(p[i] for p in points) for i in range(3)]
print("bounds", (low[0], low[2], -high[1]), (high[0], high[2], -low[1]))
for name in ['spine', 'forearm.L', 'hand.L']:
    head = rig.matrix_world @ rig.pose.bones[name].head
    print(name, (head.x, head.z, -head.y))
