"""Exercise deformation in memory and reimport exported FBX; does not add animations."""
import bpy, json, math
from pathlib import Path
from mathutils import Vector, Quaternion
root=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(root/'Kestrel_Structure_v01.blend'))
rig=bpy.data.objects['Kestrel_Rig']; bird=bpy.data.objects['Kestrel_SkinnedMesh']
scene=bpy.context.scene
baseline=[v.co.copy() for v in bird.evaluated_get(bpy.context.evaluated_depsgraph_get()).data.vertices]
def world_turn(name,axis,degrees):
    pb=rig.pose.bones[name]
    local=pb.bone.matrix_local.to_3x3().inverted() @ Vector(axis)
    pb.rotation_mode='QUATERNION';pb.rotation_quaternion=Quaternion(local,math.radians(degrees))
for side,sign in [('L',1),('R',-1)]:
    world_turn('WingUpper.'+side,(0,1,0),-sign*35)
    world_turn('WingFore.'+side,(0,1,0),-sign*12)
    world_turn('WingHand.'+side,(0,1,0),sign*8)
    world_turn('Thigh.'+side,(1,0,0),-25)
    world_turn('Shin.'+side,(1,0,0),45)
    for i in range(4):world_turn('ToeTip_%s.%s'%(i+1,side),(1,0,0),-35)
world_turn('Head',(0,0,1),15)
bpy.context.view_layer.update()
dg=bpy.context.evaluated_depsgraph_get();evaluated=bird.evaluated_get(dg).to_mesh()
moved=sum((v.co-baseline[i]).length>.0001 for i,v in enumerate(evaluated.vertices))
assert moved>1000
assert all(math.isfinite(c) for v in evaluated.vertices for c in v.co)
bird.evaluated_get(dg).to_mesh_clear()
scene.camera=bpy.data.objects['Review_ThreeQuarter']
scene.render.filepath=str(root/'Previews'/'Rig_Deformation_Check.png')
bpy.ops.render.render(write_still=True)
stats=json.loads((root/'validation.json').read_text())
stats['deformation_check']={'moved_vertices':moved,'finite_positions':True,'production_animation':False}
# Roundtrip in a clean scene confirms the exported skeleton and skin can be read.
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(root/'Export'/'Kestrel_Structure_v01.fbx'))
arms=[o for o in bpy.data.objects if o.type=='ARMATURE']
meshes=[o for o in bpy.data.objects if o.type=='MESH']
assert len(arms)==1 and len(meshes)==1
assert len(arms[0].data.bones)==stats['bones']
assert any(m.type=='ARMATURE' and m.object==arms[0] for m in meshes[0].modifiers)
assert all(v.groups for v in meshes[0].data.vertices)
stats['fbx_roundtrip']={'armatures':len(arms),'meshes':len(meshes),'bones':len(arms[0].data.bones),'skin_modifier':True}
(root/'validation.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
print('RIG_CHECK_OK',json.dumps(stats))
