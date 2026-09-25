import bpy,math,json
from pathlib import Path
from mathutils import Vector,Quaternion
out=Path(__file__).resolve().parent/'Stress';out.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(out.parent/'Kestrel_Rig_v01.blend'))
rig=bpy.data.objects['Kestrel_Rig'];mesh=bpy.data.objects['Kestrel_SkinnedMesh'];sc=bpy.context.scene
def reset():
 for p in rig.pose.bones:p.rotation_quaternion=Quaternion();p.location=(0,0,0)
def turn(n,axis,deg):
 p=rig.pose.bones[n];p.rotation_quaternion=Quaternion(p.bone.matrix_local.to_3x3().inverted()@Vector(axis),math.radians(deg))
def points():
 bpy.context.view_layer.update();o=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());m=o.to_mesh();v=[x.co.copy() for x in m.vertices];o.to_mesh_clear();return v
base=points();edges=[tuple(e.vertices) for e in mesh.data.edges];lengths=[(base[a]-base[b]).length for a,b in edges]
report={}
for name in ['HighUp','DeepDown','Fold','LegTuck','Grasp','TailTurn']:
 reset()
 for side,s in [('L',1),('R',-1)]:
  if name in ['HighUp','DeepDown']:
   a=-65 if name=='HighUp' else 60;turn('WingUpper.'+side,(0,1,0),a*s);turn('WingFore.'+side,(0,1,0),a*.15*s)
  if name=='Fold':
   turn('WingUpper.'+side,(0,1,0),70*s);turn('WingFore.'+side,(0,1,0),-120*s);turn('WingHand.'+side,(0,1,0),115*s)
  if name=='LegTuck':turn('Thigh.'+side,(1,0,0),-40);turn('Shin.'+side,(1,0,0),70);turn('Foot.'+side,(1,0,0),-25)
  if name=='Grasp':
   for d in ['Inner','Middle','Outer','Rear']:
    for j in range(1,4):turn(f'Toe{d}_{j}.{side}',(1,0,0),(1 if d!='Rear' else -1)*30)
 if name=='TailTurn':turn('Tail',(1,0,0),25);turn('TailFan.L',(0,1,0),-12);turn('TailFan.R',(0,1,0),12);turn('Head',(0,0,1),40)
 pts=points();ratios=sorted((pts[a]-pts[b]).length/l for (a,b),l in zip(edges,lengths) if l>.0002)
 report[name]={'finite':all(math.isfinite(c) for p in pts for c in p),'edge_ratio_p99':ratios[int(.99*len(ratios))],'edge_ratio_max':max(ratios),'edges_stretched_over_2x':sum(r>2 for r in ratios)}
 sc.camera=bpy.data.objects['FeetCamera' if name=='Grasp' else 'ReviewCamera'];sc.render.resolution_x=1100;sc.render.resolution_y=900;sc.render.filepath=str(out/(name+'.png'));bpy.ops.render.render(write_still=True)
(out/'metrics.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
