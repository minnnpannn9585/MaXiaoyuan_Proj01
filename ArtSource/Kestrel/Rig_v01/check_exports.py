import bpy,json
from pathlib import Path
from mathutils import Quaternion,Vector
out=Path(__file__).resolve().parent;results={}
for ext in ['fbx','glb']:
 bpy.ops.wm.read_factory_settings(use_empty=True)
 path=str(out/f'Kestrel_Rig_v01.{ext}')
 if ext=='fbx':bpy.ops.import_scene.fbx(filepath=path)
 else:bpy.ops.import_scene.gltf(filepath=path)
 rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
 mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH')
 assert len(rig.data.bones)==45
 assert len(mesh.data.vertices)==20368
 assert all(v.groups for v in mesh.data.vertices)
 assert max(abs(sum(g.weight for g in v.groups)-1) for v in mesh.data.vertices)<1e-5
 assert not bpy.data.actions
 assert any(m.type=='ARMATURE' and m.object==rig for m in mesh.modifiers)
 def pts():
  bpy.context.view_layer.update();o=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());me=o.to_mesh();vs=[v.co.copy() for v in me.vertices];o.to_mesh_clear();return vs
 before=pts();pb=rig.pose.bones['WingUpper.L'];pb.rotation_mode='QUATERNION';pb.rotation_quaternion=Quaternion(Vector((1,0,0)),.25)
 count=sum((a-b).length>1e-6 for a,b in zip(before,pts()));assert count>1000
 results[ext]={'bones':45,'vertices':len(mesh.data.vertices),'weighted_vertices':len(mesh.data.vertices),'animations':0,'armature_deforms_mesh':True,'moved_vertices':count}
(out/'export_validation.json').write_text(json.dumps(results,indent=2));print(json.dumps(results))
