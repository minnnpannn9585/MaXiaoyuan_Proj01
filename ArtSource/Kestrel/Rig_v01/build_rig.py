"""Bind the approved toes-only-v2 mesh without changing geometry. Blender 3.6."""
import bpy, bmesh, math, json, hashlib
from pathlib import Path
from mathutils import Vector, Quaternion
OUT=Path(__file__).resolve().parent;SRC=OUT.parent/'Toes_Only_v2/Kestrel_Toes_Only_v2.blend'
OUT.mkdir(exist_ok=True);(OUT/'Previews').mkdir(exist_ok=True)
source_hash=hashlib.sha256(SRC.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SRC))
mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH');bpy.context.view_layer.objects.active=mesh
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True);mesh.name='Kestrel_SkinnedMesh'
baseline=[v.co.copy() for v in mesh.data.vertices];faces=[tuple(p.vertices) for p in mesh.data.polygons]
# Classify isolated foot shells before weighting, avoiding any influence on nearby tail feathers.
adj=[set() for v in baseline]
for e in mesh.data.edges:
 a,b=e.vertices;adj[a].add(b);adj[b].add(a)
todo=set(range(len(baseline)));foot=set()
while todo:
 seed=todo.pop();g={seed};stack=[seed]
 while stack:
  for j in adj[stack.pop()]:
   if j in todo:todo.remove(j);g.add(j);stack.append(j)
 if len(g)>1000 and max(baseline[i].z for i in g)<.17 and max(baseline[i].y for i in g)<0:foot.update(g)
assert 2500<len(foot)<4000,len(foot)
arm=bpy.data.armatures.new('Kestrel_Skeleton');rig=bpy.data.objects.new('Kestrel_Rig',arm);bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig;rig.select_set(True);mesh.select_set(False);bpy.ops.object.mode_set(mode='EDIT')
spec={}
def bone(n,h,t,parent=None):
 b=arm.edit_bones.new(n);b.head=h;b.tail=t
 if parent:b.parent=arm.edit_bones[parent]
 b.use_deform=True;spec[n]=(Vector(h),Vector(t));return n
bone('Root',(0,0,0),(0,0,.06))
bone('Pelvis',(0,.025,.19),(0,-.012,.255),'Root')
bone('Chest',(0,-.012,.255),(0,-.066,.343),'Pelvis')
bone('Neck',(0,-.066,.343),(0,-.102,.379),'Chest')
bone('Head',(0,-.102,.379),(0,-.15,.405),'Neck')
bone('Tail',(0,.033,.185),(0,.10,.10),'Pelvis')
for k,x in [('L',.062),('Center',0),('R',-.062)]:bone('TailFan.'+k,(x*.28,.083,.127),(x,.176,.022),'Tail')
toe_paths={
 'Inner':[(.050,-.056,.087),(.037,-.082,.078),(.033,-.094,.065),(.034,-.095,.049)],
 'Middle':[(.053,-.057,.088),(.057,-.090,.073),(.059,-.116,.057),(.059,-.124,.037)],
 'Outer':[(.057,-.055,.088),(.075,-.079,.073),(.089,-.095,.058),(.094,-.102,.040)],
 'Rear':[(.050,-.045,.086),(.050,-.027,.082),(.050,-.015,.076),(.050,-.012,.060)]}
for side,s in [('L',1),('R',-1)]:
 def mirror(p):return (s*p[0],p[1],p[2])
 bone('WingUpper.'+side,mirror((.055,-.063,.342)),mirror((.18,-.054,.354)),'Chest')
 bone('WingFore.'+side,mirror((.18,-.054,.354)),mirror((.305,-.065,.373)),'WingUpper.'+side)
 bone('WingHand.'+side,mirror((.305,-.065,.373)),mirror((.475,-.060,.355)),'WingFore.'+side)
 bone('Thigh.'+side,mirror((.042,.008,.202)),mirror((.051,-.027,.16)),'Pelvis')
 bone('Shin.'+side,mirror((.051,-.027,.16)),mirror((.052,-.047,.102)),'Thigh.'+side)
 bone('Foot.'+side,mirror((.052,-.047,.102)),mirror((.053,-.060,.086)),'Shin.'+side)
 for digit,path in toe_paths.items():
  parent='Foot.'+side
  for j in range(3):
   name=f'Toe{digit}_{j+1}.{side}';bone(name,mirror(path[j]),mirror(path[j+1]),parent);parent=name
bpy.ops.object.mode_set(mode='OBJECT');rig.show_in_front=True;arm.display_type='OCTAHEDRAL'
groups={n:mesh.vertex_groups.new(name=n) for n in spec}
def smooth(a,b,v):
 t=max(0,min(1,(v-a)/(b-a)));return t*t*(3-2*t)
def mix(a,b,t):
 d={k:v*(1-t) for k,v in a.items()}
 for k,v in b.items():d[k]=d.get(k,0)+v*t
 return d
def segment(p,a,b):
 v=b-a;t=max(0,min(1,(p-a).dot(v)/v.length_squared));return (p-a-v*t).length
def chain_weights(p,names):
 ds=[segment(p,*spec[n]) for n in names];best=min(range(len(ds)),key=lambda j:ds[j])
 # Feathered joint zones but keep claw ends rigid.
 if best==2:return {names[2]:1.0} if (p-spec[names[2]][0]).dot(spec[names[2]][1]-spec[names[2]][0])>0 else {names[1]:.3,names[2]:.7}
 a,b=spec[names[best]];t=(p-a).dot(b-a)/(b-a).length_squared
 if t>.65 and best<2:return {names[best]:1-smooth(.65,1.15,t),names[best+1]:smooth(.65,1.15,t)}
 if t<.18 and best>0:return {names[best-1]:1-smooth(-.18,.18,t),names[best]:smooth(-.18,.18,t)}
 return {names[best]:1}
weights=[]
for i,p in enumerate(baseline):
 x,y,z=p;ax=abs(x);side='L' if x>=0 else 'R'
 if i in foot:
  if z>.105:w=mix({'Shin.'+side:1},{'Foot.'+side:1},1-smooth(.103,.125,z))
  else:
   paths={d:[f'Toe{d}_{j+1}.{side}' for j in range(3)] for d in toe_paths}
   d=min(paths,key=lambda d:min(segment(p,*spec[n]) for n in paths[d]))
   tw=chain_weights(p,paths[d]);distance=(p-Vector((.052*(1 if x>=0 else -1),-.05,.090))).length
   w=mix({'Foot.'+side:1},tw,smooth(.006,.020,distance))
 else:
  w=mix({'Pelvis':1},{'Chest':1},smooth(.22,.29,z))
  w=mix(w,{'Neck':1},smooth(.333,.370,z));w=mix(w,{'Head':1},smooth(.365,.389,z))
  if y>.035 and z<.23:
   fan=mix({'TailFan.Center':1},{'TailFan.'+side:1},smooth(.008,.060,ax))
   tail=mix({'Tail':1},fan,smooth(.085,.14,y))
   w=mix(w,tail,smooth(.035,.075,y))
  if ax>.021 and z<.235 and y<.044:
   leg=mix({'Thigh.'+side:1},{'Shin.'+side:1},1-smooth(.145,.175,z))
   w=mix(w,leg,(1-smooth(.185,.225,z))*smooth(.021,.038,ax)*(1-smooth(.025,.044,y)))
  if z>.195 and ax>.055:
   wing=mix({'WingUpper.'+side:1},{'WingFore.'+side:1},smooth(.155,.225,ax))
   wing=mix(wing,{'WingHand.'+side:1},smooth(.285,.35,ax))
   w=mix(w,wing,smooth(.055,.11,ax))
 w=dict(sorted(((n,v) for n,v in w.items() if v>1e-5),key=lambda x:-x[1])[:4]);total=sum(w.values())
 weights.append({n:v/total for n,v in w.items()})
# Smooth only skin weights across connected foot vertices. No vertex positions are modified.
for iteration in range(12):
 nextweights=list(weights)
 for i in foot:
  if not adj[i]:continue
  avg={}
  for j in adj[i]:
   for n,v in weights[j].items():avg[n]=avg.get(n,0)+v/len(adj[i])
  w=mix(weights[i],avg,.5)
  w=dict(sorted(w.items(),key=lambda kv:-kv[1])[:4]);total=sum(w.values())
  nextweights[i]={n:v/total for n,v in w.items()}
 weights=nextweights
for i,w in enumerate(weights):
 for n,v in w.items():groups[n].add([i],v,'REPLACE')
mesh.parent=rig;mod=mesh.modifiers.new('Kestrel Skin','ARMATURE');mod.object=rig
# Linear skinning matches standard Unity deformation.
mod.use_deform_preserve_volume=False
for pb in rig.pose.bones:pb.rotation_mode='QUATERNION'
rig['Notes']='FK rig. No animation. Rest mesh unchanged. Merged wing/tail feather sheets limit extreme folding. Beak is rigid to Head.'
sc=bpy.context.scene;sc.render.engine='BLENDER_WORKBENCH';sh=sc.display.shading;sh.light='STUDIO';sh.studio_light='paint.sl';sh.color_type='SINGLE';sh.single_color=(.5,.5,.5);sh.show_cavity=True;sh.cavity_type='BOTH';sh.background_type='WORLD';sc.world=bpy.data.worlds.new('Review');sc.world.color=(.07,.07,.07)
sc.render.resolution_x=1400;sc.render.resolution_y=1000;sc.render.resolution_percentage=100
def camera(name,center,offset,scale):
 bpy.ops.object.camera_add(location=Vector(center)+Vector(offset));c=bpy.context.object;c.name=name;c.rotation_euler=(Vector(center)-c.location).to_track_quat('-Z','Y').to_euler();c.data.type='ORTHO';c.data.ortho_scale=scale;return c
maincam=camera('ReviewCamera',(0,0,.32),(1,-2,.9),1.35)
feetcam=camera('FeetCamera',(0,-.060,.082),(.25,-1,-.25),.24)
sc.camera=maincam
def select_export():
 bpy.ops.object.select_all(action='DESELECT');mesh.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
select_export()
for area in bpy.context.screen.areas:
 if area.type=='VIEW_3D':
  area.spaces.active.region_3d.view_distance=1.25;area.spaces.active.region_3d.view_location=(0,0,.22)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Kestrel_Rig_v01.blend'))
bpy.ops.export_scene.fbx(filepath=str(OUT/'Kestrel_Rig_v01.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',use_armature_deform_only=True)
bpy.ops.export_scene.gltf(filepath=str(OUT/'Kestrel_Rig_v01.glb'),export_format='GLB',use_selection=True,export_animations=False)
def evaluated():
 bpy.context.view_layer.update();obj=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());me=obj.to_mesh();result=[v.co.copy() for v in me.vertices];obj.to_mesh_clear();return result
rest=evaluated();assert max((a-b).length for a,b in zip(rest,baseline))<1e-6
assert faces==[tuple(p.vertices) for p in mesh.data.polygons]
def render(n,c=maincam):
 sc.camera=c;sc.render.filepath=str(OUT/'Previews'/f'{n}.png');bpy.ops.render.render(write_still=True)
render('Rest')
def turn(n,axis,deg):
 pb=rig.pose.bones[n];pb.rotation_quaternion=Quaternion(pb.bone.matrix_local.to_3x3().inverted()@Vector(axis),math.radians(deg))
def reset():
 for pb in rig.pose.bones:pb.rotation_quaternion=Quaternion((1,0,0,0))
checks=[]
for label,angle in [('WingsUp',-45),('WingsDown',40)]:
 reset()
 for side,s in [('L',1),('R',-1)]:turn('WingUpper.'+side,(0,1,0),angle*s);turn('WingFore.'+side,(0,1,0),angle*.25*s)
 turn('Head',(0,0,1),15);turn('Tail',(1,0,0),10)
 pts=evaluated();assert all(math.isfinite(v) for p in pts for v in p)
 checks.append({'pose':label,'moved_vertices':sum((a-b).length>1e-5 for a,b in zip(pts,baseline))})
 render(label)
reset()
for side in ['L','R']:
 for d in toe_paths:
  for j,deg in [(1,15),(2,25),(3,15)]:turn(f'Toe{d}_{j}.{side}',(1,0,0),-deg if d=='Rear' else deg)
render('ToeCurl',feetcam);checks.append({'pose':'ToeCurl','moved_vertices':sum((a-b).length>1e-5 for a,b in zip(evaluated(),baseline))})
reset();assert max((a-b).length for a,b in zip(evaluated(),baseline))<1e-6
report={'source':str(SRC),'source_unchanged':source_hash==hashlib.sha256(SRC.read_bytes()).hexdigest(),'geometry_unchanged':True,'vertices':len(baseline),'faces':len(faces),'bones':len(arm.bones),'max_influences':max(len(v.groups) for v in mesh.data.vertices),'unweighted_vertices':sum(not v.groups for v in mesh.data.vertices),'max_weight_sum_error':max(abs(sum(g.weight for g in v.groups)-1) for v in mesh.data.vertices),'animations':len(bpy.data.actions),'pose_checks':checks}
(OUT/'validation.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
