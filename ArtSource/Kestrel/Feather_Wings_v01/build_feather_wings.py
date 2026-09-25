import bpy,bmesh,math,json,hashlib
from pathlib import Path
from mathutils import Vector,Quaternion,Matrix
from mathutils.bvhtree import BVHTree
OUT=Path(__file__).resolve().parent;ROOT=OUT.parents[2];STUDY=ROOT/'Temp/WingStudy'
OUT.mkdir(exist_ok=True);(OUT/'Previews').mkdir(exist_ok=True)
SRC=OUT.parent/'Rig_v01/Kestrel_Rig_v01.blend';bpy.ops.wm.open_mainfile(filepath=str(SRC))
rig=bpy.data.objects['Kestrel_Rig'];ob=bpy.data.objects['Kestrel_SkinnedMesh'];sc=bpy.context.scene
orig=[v.co.copy() for v in ob.data.vertices];original_faces=[list(p.vertices) for p in ob.data.polygons]
vgname={g.index:g.name for g in ob.vertex_groups};ow=[{vgname[g.group]:g.weight for g in v.groups} for v in ob.data.vertices]
bvh=BVHTree.FromPolygons(orig,original_faces,all_triangles=False)
def surface_y(x,z):
 # Smooth fitted midsurface avoids discontinuous ray hits at the source's thin serrated edge.
 return .123-.60*z-.017*abs(x)
# Determine the main continuous shell. Covert feather details remain untouched.
adj=[set() for v in orig]
for e in ob.data.edges:
 a,b=e.vertices;adj[a].add(b);adj[b].add(a)
unseen=set(range(len(orig)));components=[]
while unseen:
 g={unseen.pop()};stack=list(g)
 while stack:
  for j in adj[stack.pop()]:
   if j in unseen:unseen.remove(j);g.add(j);stack.append(j)
 components.append(g)
main=max(components,key=len)
def lerp(a,b,t):
 c=a[0].lerp(b[0],t);w={n:a[1].get(n,0)*(1-t)+b[1].get(n,0)*t for n in a[1].keys()|b[1].keys()};return c,w
def clip(poly,fn):
 result=[]
 for a,b in zip(poly,poly[1:]+poly[:1]):
  da,db=fn(a[0]),fn(b[0]);ia,ib=da>=-1e-9,db>=-1e-9
  if ia:result.append(a)
  if ia!=ib:result.append(lerp(a,b,da/(da-db)))
 return result
newv=[];newf=[];neww=[];lookup={};protected=[]
def addpoly(poly):
 if len(poly)<3:return
 inds=[]
 for co,w in poly:
  key=tuple(round(c,9) for c in co)
  if key not in lookup:lookup[key]=len(newv);newv.append(tuple(co));neww.append(w)
  inds.append(lookup[key])
 if len(set(inds))>=3:newf.append(inds)
for p in ob.data.polygons:
 poly=[(orig[i],ow[i]) for i in p.vertices];sx=1 if sum(v[0].x for v in poly)>0 else -1
 iswing=any(sx*v[0].x>.075 for v in poly) and all(v[0].z>.19 for v in poly)
 if not iswing or p.vertices[0] not in main:addpoly(poly);protected.append([tuple(orig[i]) for i in p.vertices]);continue
 addpoly(clip(poly,lambda v:.075-sx*v.x))
 wing=clip(poly,lambda v:sx*v.x-.075)
 wing=clip(wing,lambda v:v.z-(.339+.05*(sx*v.x-.075)))
 wing=clip(wing,lambda v:.332-sx*v.x)
 addpoly(wing)
me=bpy.data.meshes.new('Kestrel_Body_Coverts');me.from_pydata(newv,[],newf);me.update();ob.data=me;ob.name='Kestrel_Body_Coverts'
for g in list(ob.vertex_groups):ob.vertex_groups.remove(g)
for n in vgname.values():ob.vertex_groups.new(name=n)
for i,w in enumerate(neww):
 for n,v in w.items():
  if v>1e-6:ob.vertex_groups[n].add([i],v,'REPLACE')
for p in me.polygons:p.use_smooth=True
# The three wing joints now bend within the rest wing plane, rather than world Y alone.
bpy.context.view_layer.objects.active=rig;ob.select_set(False);rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
for side,s in [('L',1),('R',-1)]:
 coords=[(.057,-.066,.344),(.168,-.027,.326),(.298,-.075,.368),(.467,-.047,.350)]
 for i,n in enumerate(['WingUpper','WingFore','WingHand']):
  b=rig.data.edit_bones[n+'.'+side];b.head=(s*coords[i][0],*coords[i][1:]);b.tail=(s*coords[i+1][0],*coords[i+1][1:])
plans=json.loads((STUDY/'feather_plans.json').read_text());feather_specs=[]
for side,s in [('L',1),('R',-1)]:
 for plan in plans:
  j=plan['index'];rx,rz=plan['root'];tx,tz=plan['tip'];name=f'FlightFeather_{j+1:02d}.{side}'
  h=Vector((s*rx,surface_y(s*rx,rz),rz));t=Vector((s*tx,surface_y(s*tx,tz),tz))
  b=rig.data.edit_bones.new(name);b.head=h;b.tail=t;b.parent=rig.data.edit_bones[('WingUpper' if j<5 else 'WingFore' if j<12 else 'WingHand')+'.'+side]
  b.align_roll(Vector((0,-1,0)));feather_specs.append((name,side,s,plan,h,t))
bpy.ops.object.mode_set(mode='OBJECT')
feathers=[];records=[]
for name,side,s,plan,h,t in feather_specs:
 j=plan['index'];points=plan['verts'];tri=plan['faces'];v=[]
 # Thin closed vanes follow the original curved wing surface. A slight ordered offset creates underlap.
 for layer in [-1,1]:
  for x,z in points:
   y=surface_y(s*x,z);offset=(j-9)*.00038
   v.append((s*x,y+offset+layer*.00012,z))
 n=len(points);f=[];edges={}
 for a,b,c in tri:
  f.extend([(a,b,c),(c+n,b+n,a+n)])
  for q,r in [(a,b),(b,c),(c,a)]:
   key=tuple(sorted((q,r)));edges.setdefault(key,[]).append((q,r))
 for edge,occ in edges.items():
  if len(occ)==1:
   a,b=occ[0];f.append((b,a,a+n,b+n))
 mesh=bpy.data.meshes.new(name);mesh.from_pydata(v,[],f);mesh.update()
 bm=bmesh.new();bm.from_mesh(mesh)
 bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-7)
 bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=1e-8)
 loose=[e for e in bm.edges if not e.link_faces]
 if loose:bmesh.ops.delete(bm,geom=loose,context='EDGES')
 bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));nonmanifold=sum(not e.is_manifold for e in bm.edges)
 if nonmanifold:print(name,[(e.calc_length(),len(e.link_faces),[tuple(v.co) for v in e.verts]) for e in bm.edges if not e.is_manifold],flush=True)
 bm.to_mesh(mesh);bm.free()
 assert nonmanifold==0,(name,nonmanifold)
 obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj);obj.parent=rig
 g=obj.vertex_groups.new(name=name);g.add(list(range(len(mesh.vertices))),1,'REPLACE');m=obj.modifiers.new('Rigid feather skin','ARMATURE');m.object=rig
 for p in mesh.polygons:p.use_smooth=True
 feathers.append(obj);records.append({'name':name,'vertices':len(v),'closed_mesh':True,'weight':name})
# Add a user-facing fold control on each wing root; quaternion drivers distribute the motion.
def setrot(name,axis,angle):
 p=rig.pose.bones[name];p.rotation_mode='QUATERNION';local=p.bone.matrix_local.to_3x3().inverted()@Vector(axis);p.rotation_quaternion=Quaternion(local,math.radians(angle))
normal=Vector((0,.76,.65)).normalized()
for side,s in [('L',1),('R',-1)]:
 for n,a in [('WingUpper',78),('WingFore',-130),('WingHand',135)]:setrot(n+'.'+side,normal,a*s)
bpy.context.view_layer.update()
for name,side,s,plan,h,t in feather_specs:
 p=rig.pose.bones[name];mat=p.matrix.copy();head=mat.translation;idx=plan['index']
 # Aim each rigid feather aft/down, with graded fan angles rather than bending its vane.
 head.x=s*(.078+.00065*idx)
 desired=Vector((s*.025,.63,-.776)).normalized()
 tangent=desired.cross(Vector((s,0,0))).normalized();normal_closed=tangent.cross(desired).normalized()
 target=Matrix((tangent,desired,normal_closed)).transposed().to_4x4();target.translation=head;p.matrix=target;bpy.context.view_layer.update()
closed={p.name:p.rotation_quaternion.copy() for p in rig.pose.bones if p.name.startswith(('Wing','FlightFeather'))}
closed_locations={p.name:p.location.copy() for p in rig.pose.bones if p.name.startswith('FlightFeather')}
for p in rig.pose.bones:p.rotation_quaternion=Quaternion();p.location=(0,0,0)
for side in ['L','R']:
 rig['Fold_'+side]=0.0;rig.id_properties_ui('Fold_'+side).update(min=0,max=1,description='0 = spread; 1 = folded. Rigid feather vanes overlap independently.')
for name,q in closed.items():
 if q.w<0:q.negate()
 angle=math.acos(max(-1,min(1,q.w)));side=name[-1];pb=rig.pose.bones[name]
 for i in range(4):
  fc=pb.driver_add('rotation_quaternion',i);d=fc.driver;v=d.variables.new();v.name='f';v.targets[0].id=rig;v.targets[0].data_path=f'["Fold_{side}"]'
  d.expression=f'cos({angle:.12f}*f)' if i==0 else (f'{q[i]/math.sin(angle):.12f}*sin({angle:.12f}*f)' if abs(angle)>1e-7 else '0')
for name,loc in closed_locations.items():
 pb=rig.pose.bones[name]
 for i in range(3):
  fc=pb.driver_add('location',i);d=fc.driver;v=d.variables.new();v.name='f';v.targets[0].id=rig;v.targets[0].data_path=f'["Fold_{name[-1]}"]';d.expression=f'{loc[i]:.12f}*f'
rig['Notes']='Independent closed flight feather vanes. Fold_L / Fold_R control wing and feather overlap. Test prototype; no animation clips.'
bpy.context.view_layer.update();sc.render.engine='BLENDER_WORKBENCH';sc.render.resolution_x=1500;sc.render.resolution_y=1100;sc.render.resolution_percentage=100
def camera(name,center,offset,scale):
 c=bpy.data.objects.get(name)
 if c is None:bpy.ops.object.camera_add();c=bpy.context.object;c.name=name
 c.location=Vector(center)+Vector(offset);c.rotation_euler=(Vector(center)-c.location).to_track_quat('-Z','Y').to_euler();c.data.type='ORTHO';c.data.ortho_scale=scale;return c
cam=camera('WingReview',(0,0,.25),(1,-2,.9),1.15);sidecam=camera('SideReview',(0,0,.25),(1,0,.08),.63)
def render(n,c):
 sc.camera=c;sc.render.filepath=str(OUT/'Previews'/f'{n}.png');bpy.ops.render.render(write_still=True)
render('Spread',cam)
for a in [.5,1.0]:
 rig['Fold_L']=a;rig['Fold_R']=a;rig.update_tag();bpy.context.view_layer.update();render('Fold_'+str(a),cam)
render('Fold_Side',sidecam)
rig['Fold_L']=0.0;rig['Fold_R']=0.0;rig.update_tag();bpy.context.view_layer.update();sc.camera=cam
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Kestrel_Feather_Wings_v01.blend'))
(OUT/'validation.json').write_text(json.dumps({'source':str(SRC),'feather_count':len(feathers),'feathers':records,'protected_original_faces':len(protected),'animations':len(bpy.data.actions)},indent=2))
print('DONE',len(feathers),flush=True)
