"""Blender 3.6: original kestrel model, material atlas and STATIC review shapes.
No animation clips. Wing fold is an editable modeling shape, not an animation rig.
"""
import bpy, math, random, json
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parent
random.seed(76)
bpy.ops.wm.read_factory_settings(use_empty=True)
sc=bpy.context.scene;sc.unit_settings.system='METRIC'
parts=[]; fold_positions={}

def mat(name,col,rough=.65):
 m=bpy.data.materials.new(name);m.use_nodes=True;m.diffuse_color=(*col,1)
 p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*col,1);p.inputs['Roughness'].default_value=rough;p.inputs['Specular'].default_value=.26
 return m
plume=mat('Neutral_Clay',(.36,.38,.40),.85)
horn=mat('Horn_Slate',(.065,.082,.09),.37)
gold=mat('Cere_MutedOchre',(.55,.34,.042),.58)
feetmat=mat('Feet_Ochre',(.41,.24,.045),.65)
black=mat('Eye_DarkBrown',(.008,.006,.004),.15)
black.node_tree.nodes.get('Principled BSDF').inputs['Specular'].default_value=.46
rim=mat('Orbital_Skin',(.32,.245,.065),.65)
nostril=mat('Nostril',(.015,.012,.009),.78)
claws=mat('Claws',(.035,.026,.018),.42)
# Pixel-observed panel boundaries in the generated atlas (top-origin coordinates).
zones={'back':(.006,.008,.493,.215),'breast':(.505,.008,.992,.215),'crown':(.006,.23,.492,.404),'cheek':(.506,.23,.994,.404),'primaryA':(.004,.42,.246,.986),'primaryB':(.254,.42,.496,.986),'secondary':(.505,.42,.744,.986),'tail':(.755,.42,.994,.989)}
def atlas(zone,u,v):
 x0,y0,x1,y1=zones[zone]
 return (x0+(x1-x0)*max(0,min(1,u)),1-(y0+(y1-y0)*max(0,min(1,v))))

def mesh(name,verts,faces,uvs,material=plume,fold=None):
 me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
 ob=bpy.data.objects.new(name,me);sc.collection.objects.link(ob);me.materials.append(material)
 for f in me.polygons:f.use_smooth=True
 if uvs:
  uv=me.uv_layers.new(name='UVMap')
  for f in me.polygons:
   for l in f.loop_indices:uv.data[l].uv=uvs[me.loops[l].vertex_index]
 parts.append(ob)
 fold_positions[ob.as_pointer()]=fold if fold is not None else [tuple(v) for v in verts]
 return ob
def sphere(name,c,s,m=plume,seg=40,rings=24):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=seg,ring_count=rings,location=c)
 ob=bpy.context.object;ob.name=name;ob.scale=s;bpy.ops.object.transform_apply(location=True,rotation=True,scale=True);ob.data.materials.append(m)
 for f in ob.data.polygons:f.use_smooth=True
 parts.append(ob);fold_positions[ob.as_pointer()]=[tuple(v.co) for v in ob.data.vertices]
 return ob
def tube(name,pts,rads,material,sides=12):
 vs=[];fs=[];uv=[]
 for i,pt in enumerate(pts):
  pt=Vector(pt);d=(Vector(pts[min(i+1,len(pts)-1)])-Vector(pts[max(0,i-1)])).normalized()
  a=d.cross(Vector((0,0,1)))
  if a.length<.01:a=d.cross(Vector((1,0,0)))
  a.normalize();b=d.cross(a)
  for j in range(sides):
   t=j*math.tau/sides;vs.append(tuple(pt+rads[i]*(a*math.cos(t)+b*math.sin(t))));uv.append((j/sides,i/(len(pts)-1)))
   if i:
    k=i*sides+j;n=i*sides+(j+1)%sides;fs.append((k-sides,n-sides,n,k))
 fs.extend([tuple(reversed(range(sides))),tuple(range(len(vs)-sides,len(vs)))])
 return mesh(name,vs,fs,uv,material)

# Longitudinal anatomical sections: one continuous envelope, no fused balls.
# y, center-height, lateral half-width, dorsal radius, ventral radius
sections=[(-.112,.226,.003,.003,.004),(-.106,.229,.013,.012,.009),
 (-.096,.232,.021,.018,.017),(-.082,.232,.024,.021,.023),
 (-.068,.224,.023,.024,.026),(-.054,.207,.026,.032,.031),
 (-.037,.190,.030,.032,.035),(-.015,.181,.033,.032,.037),
 (.008,.177,.033,.034,.034),(.030,.174,.028,.030,.028),
 (.052,.169,.021,.023,.021),(.070,.166,.013,.014,.013),(.086,.162,.002,.003,.002)]
def catmull(a,b,c,d,t):return .5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t)
profiles=[]
for i in range(len(sections)-1):
 for j in range(5):
  profiles.append([catmull(sections[max(0,i-1)][k],sections[i][k],sections[i+1][k],sections[min(len(sections)-1,i+2)][k],j/5) for k in range(5)])
profiles.append(sections[-1])
vs=[];fs=[];uvs=[];sides=48
for i,(y,z,w,up,down) in enumerate(profiles):
 for j in range(sides):
  a=math.tau*j/sides;zz=math.cos(a);x=w*math.sin(a)
  vs.append((x,y,z+zz*(up if zz>0 else down)));uvs.append((j/sides,i/(len(profiles)-1)))
  if i:
   k=i*sides+j;n=i*sides+(j+1)%sides;fs.append((k-sides,n-sides,n,k))
fs.append(tuple(reversed(range(sides))));fs.append(tuple(range(len(vs)-sides,len(vs))))
body=mesh('Anatomical_Body',vs,fs,uvs)
bvh=BVHTree.FromPolygons([v.co for v in body.data.vertices],[list(p.vertices) for p in body.data.polygons])

# Eye placement follows the actual skull surface rather than floating spheres.
for side in (-1,1):
 hit=bvh.ray_cast(Vector((side*.08,-.092,.238)),Vector((-side,0,0)))[0]
 ex=abs(hit.x)
 sphere('Orbital_Rim',(side*(ex-.0001),-.092,.238),(.0011,.0052,.0047),plume,36,20)
 sphere('Eye',(side*(ex+.0006),-.092,.238),(.0011,.0045,.0040),black,36,20)
# Falcon bill: broad attachment, compressed sides and a short hooked upper tip.
billrows=[(-.105,.228,.008,.005),(-.114,.229,.007,.005),(-.121,.226,.005,.004),(-.125,.221,.003,.0035),(-.125,.215,.00025,.00025)]
vs=[];fs=[]
for i,(y,z,w,h) in enumerate(billrows):
 for j in range(24):
  a=math.tau*j/24;vs.append((w*math.sin(a),y,z+h*math.cos(a)))
  if i:
   k=i*24+j;n=i*24+(j+1)%24;fs.append((k-24,k,n,n-24))
mesh('Upper_Bill',vs,fs,None,plume)
tube('Lower_Bill',[(0,-.107,.222),(0,-.115,.220),(0,-.122,.219)],[.0045,.003,.0003],plume,20)
for side in (-1,1):sphere('Nostril',(side*.0064,-.113,.231),(.0004,.0010,.00065),black,16,10)

def feather_geometry(a,e,width,normal,rows=16,cols=6,asym=.44,bend=.08):
 a=Vector(a);d=Vector(e)-a;n=Vector(normal).normalized();side=d.cross(n).normalized()
 vs=[];uv=[]
 for i in range(rows+1):
  t=i/rows
  # A rounded natural end rather than triangular plates; broad vane then rapid taper.
  prof=(.22+.78*min(1,t/.17))*(max(0,1-t**5)**.58)
  prof=max(.006,prof)
  mid=a+d*t+n*width*bend*math.sin(math.pi*t)+side*width*.09*t*t
  for j in range(cols+1):
   u=j/cols;w=(u-.5)*width*prof*(2*asym if u<.5 else 2*(1-asym))
   p=mid+side*w+n*(width*.025*(1-abs(2*u-1)))-n*width*.035*abs(2*u-1)**2
   vs.append(tuple(p));uv.append((u,t))
 fs=[]
 for i in range(rows):
  for j in range(cols):
   k=i*(cols+1)+j;fs.append((k,k+1,k+cols+2,k+cols+1))
 return vs,fs,uv
def feather(name,a,e,width,zone,folda=None,folde=None,normal=(0,0,1),foldnormal=None,rows=14,cols=4,asym=.44):
 vs,fs,uvs=feather_geometry(a,e,width,normal,rows,cols,asym)
 fv,_,_=feather_geometry(folda or a,folde or e,width,foldnormal or normal,rows,cols,asym)
 # Two sides as real surfaces for engine/export. No transparent hair cards.
 count=len(vs);n=Vector(normal).normalized();fn=Vector(foldnormal or normal).normalized()
 vs += [tuple(Vector(v)-n*.00012) for v in vs[:count]]
 fv += [tuple(Vector(v)-fn*.00012) for v in fv[:count]]
 fs += [tuple(count+k for k in reversed(f)) for f in fs[:]]
 # A covert is one feather, not a miniature copy of the entire feather field.
 if zone=='back':
  offset=random.choice((.15,.36,.57,.77))
  primary_uv=[atlas(zone,offset+(u-.5)*.16,.25+v*.36) for u,v in uvs]
 else:primary_uv=[atlas(zone,u,v) for u,v in uvs]
 return mesh(name,vs,fs,primary_uv+primary_uv,plume,fv)

# Narrow, falcon-like wings. Each flight feather has its own length and overlap.
for s,label in [(1,'L'),(-1,'R')]:
 shoulder=Vector((s*.027,-.019,.197));elbow=Vector((s*.105,.008,.191));wrist=Vector((s*.197,-.032,.194));hand=Vector((s*.245,-.044,.190))
 foldshoulder=Vector((s*.023,-.033,.202));foldelbow=Vector((s*.034,-.002,.187));foldwrist=Vector((s*.031,.039,.176));foldhand=Vector((s*.021,.077,.162))
 # Smooth skinned leading wing; broad enough to conceal feather roots.
 sections=[(shoulder,.026,.009),(elbow,.051,.007),(wrist,.031,.005),(hand,.018,.002)]
 fsections=[foldshoulder,foldelbow,foldwrist,foldhand]
 vs=[];fv=[];fs=[];uvs=[]
 for i,(c,depth,thick) in enumerate(sections):
  for j in range(16):
   angle=j*math.tau/16
   vs.append(tuple(c+Vector((0,depth*(.5+.5*math.cos(angle)),thick*math.sin(angle)))))
   fv.append(tuple(fsections[i]+Vector((s*.008*math.cos(angle),depth*.15,.007*math.sin(angle)))))
   uvs.append(atlas('back',i/3,j/15))
   if i:
    k=i*16+j;n=i*16+(j+1)%16;fs.append((k-16,n-16,n,k))
 mesh('WingCore.'+label,vs,fs,uvs,plume,fv)
 for i in range(10):
  t=i/9;a=wrist.lerp(hand,t)
  # Inner to outer: wing tip becomes progressively longer and narrower.
  e=Vector((s*(.223+.128*math.sin(t*math.pi*.62)),.106-.12*t,.184+.014*t*t))
  fa=Vector((s*(.033-.0006*i),.032+.003*i,.184-.0016*i))
  fe=Vector((s*(.016+.0007*i),.139+.003*i,.146-.0015*i))
  feather('Primary_%02d.%s'%(i+1,label),a,e,.024-.006*t,'primaryA' if i%2 else 'primaryB',fa,fe,foldnormal=(s*.65,0,.75),rows=20,cols=6,asym=.37)
 for i in range(10):
  t=i/9;a=elbow.lerp(wrist,t);e=a+Vector((s*.011,.101-.008*t,-.008))
  fa=foldelbow.lerp(foldwrist,t)+Vector((s*.001,0,.0005))
  fe=Vector((s*(.029-.005*t),.097+.026*t,.151-.006*t))
  feather('Secondary_%02d.%s'%(i+1,label),a,e,.023,'secondary',fa,fe,foldnormal=(s*.7,0,.7),rows=16,cols=4)
 for i in range(6):
  t=i/6;a=shoulder.lerp(elbow,t)+Vector((0,.008,.003));e=a+Vector((s*.010,.073,-.009))
  fa=foldshoulder.lerp(foldelbow,t)+Vector((s*.005,0,.003));fe=fa+Vector((s*.003,.062,-.025))
  feather('Tertial_%02d.%s'%(i,label),a,e,.024,'back',fa,fe,foldnormal=(s*.8,0,.6),rows=10)
 for row in range(2):
  for i in range(23):
   t=i/22
   if t<.42:a=shoulder.lerp(elbow,t/.42);fa=foldshoulder.lerp(foldelbow,t/.42)
   else:a=elbow.lerp(hand,(t-.42)/.58);fa=foldelbow.lerp(foldhand,(t-.42)/.58)
   a+=Vector((0,.012+row*.022,.009-row*.001))
   fa+=Vector((s*(.003+row*.001),.013*row,.008-row*.003))
   e=a+Vector((s*.006,.032+row*.014,-.002));fe=fa+Vector((s*-.003,.040,-.018))
   feather('Covert_%s_%s.%s'%(row,i,label),a,e,.019+row*.004,'back',fa,fe,foldnormal=(s*.7,0,.7),rows=8,cols=4)
 # Legs blend into belly feathers, knees mostly hidden.
 hip=(s*.018,.008,.150);ankle=(s*.021,.029,.128);foot=(s*.021,.004,.096)
 leg=sphere('LegCover.'+label,(s*.019,.016,.144),(.010,.020,.020),plume,28,18)
 for l in leg.data.uv_layers.active.data:l.uv=atlas('breast',l.uv.x,l.uv.y)
 tube('Tarsus.'+label,[ankle,(s*.021,.018,.109),foot],[.0038,.0030,.0035],feetmat)
 for i in range(4):
  dx=(i-1)*.010 if i<3 else -s*.005
  end=Vector((s*.021+dx,-.027-(.007 if i==1 else 0) if i<3 else .025,.087))
  mid=Vector(foot).lerp(end,.52);mid.z=.088
  tube('Toe_%s.%s'%(i,label),[foot,mid,end],[.0026,.002,.0014],feetmat)
  direction=-1 if i<3 else 1
  tube('Talon_%s.%s'%(i,label),[end,end+Vector((0,direction*.004,-.001)),end+Vector((0,direction*.006,-.005))],[.0017,.001,.00015],claws,10)
  for k in range(0):
   pt=mid.lerp(end,k/7)+Vector((0,0,.0016))
   sphere('ToeScale',pt,(.0018,.00075,.00035),gold,8,4)
 for k in range(0):
  pt=Vector(ankle).lerp(Vector(foot),.2+k*.07);pt.y-=.0025
  sphere('TarsusScale',pt,(.0025,.0002,.0008),feetmat,10,6)

# Rounded barred kestrel tail, never the forked red-kite tail from the quality example.
for i in range(12):
 t=(i-5.5)/5.5;a=(t*.012,.066,.17);e=(t*.048,.225-.009*t*t,.145)
 fa=(t*.010,.066,.17);fe=(t*.016,.224-.004*abs(t),.149)
 feather('Rectrix_%02d'%i,a,e,.018,'tail',fa,fe,rows=22,cols=6)
for i in range(9):
 x=(i-4)*.005
 feather('RumpFeather_%s'%i,(x,.038,.194),(x*.9,.105,.173),.016,'back',rows=10)

# A static perch silhouette: torso rises and tail drops, with legs attached at hips.
from mathutils import Matrix
perch=Matrix.Rotation(math.radians(-25),3,'X');pivot=Vector((0,.018,.145))
def compact_head(v):
 v=Vector(v);w=max(0,min(1,(-v.y-.038)/.035));w=w*w*(3-2*w)
 return v+Vector((0,.008*w,-.009*w))
for ob in parts:
 for v in ob.data.vertices:v.co=compact_head(v.co)
 fold_positions[ob.as_pointer()]=[tuple(compact_head(v)) for v in fold_positions[ob.as_pointer()]]
 if ob.name.startswith(('Toe_','Talon_','Tarsus.')):continue
 fold_positions[ob.as_pointer()]=[tuple(pivot+perch@(Vector(v)-pivot)) for v in fold_positions[ob.as_pointer()]]

# Modeling fold target; deliberately no keyframes, actions or animation clips.
for ob in parts:
 if len(ob.data.vertices)!=len(fold_positions[ob.as_pointer()]):raise RuntimeError(ob.name+' fold topology mismatch')
 ob.shape_key_add(name='Basis')
 key=ob.shape_key_add(name='Folded_Wings_Review')
 for v,co in zip(key.data,fold_positions[ob.as_pointer()]):v.co=co
bpy.ops.object.select_all(action='DESELECT')
for ob in parts:ob.select_set(True)
bpy.context.view_layer.objects.active=body;bpy.ops.object.join();bird=bpy.context.object;bird.name='Kestrel_Model_v03'
bird['Scope']='Model only. Fold shape is for silhouette review; no animation rig or clips.'
bird['Reference']='Brown barred common kestrel; RedKite demonstration is quality reference only.'
bird['Texture']='None; geometry assessment only'
bird.active_shape_key_index=0

# Review lighting stays soft to reveal texture without hiding geometry.
studio=bpy.data.collections.new('Review_Studio');sc.collection.children.link(studio)
def studio_obj(o):
 for c in list(o.users_collection):c.objects.unlink(o)
 studio.objects.link(o)
def cam(name,loc,target,scale):
 bpy.ops.object.camera_add(location=loc);o=bpy.context.object;o.name=name;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();o.data.type='ORTHO';o.data.ortho_scale=scale;studio_obj(o);return o
cams=[cam('Open_ThreeQuarter',(.52,-.70,.65),(0,.03,.16),.83),cam('Dorsal',(0,.03,1),(0,.03,.16),.82),cam('Head_Closeup',(.36,-.46,.29),(0,-.063,.218),.17),cam('Folded_ThreeQuarter',(.44,-.58,.35),(0,.03,.15),.40),cam('Folded_Side',(.8,.0,.24),(0,.01,.17),.38)]
for name,loc,power,size in [('Soft_Key',(.15,-.6,.85),12,.65),('Soft_Fill',(-.6,-.15,.4),5,.6),('Rim',(.2,.6,.65),9,.5)]:
 bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;o.name=name;o.data.energy=power;o.data.shape='DISK';o.data.size=size;o.rotation_euler=(Vector((0,0,.16))-o.location).to_track_quat('-Z','Y').to_euler();studio_obj(o)
world=bpy.data.worlds.new('Neutral_Studio');sc.world=world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.27,.29,.31,1);world.node_tree.nodes['Background'].inputs[1].default_value=.7
sc.render.engine='BLENDER_EEVEE';sc.eevee.use_gtao=True;sc.eevee.gtao_distance=.025;sc.eevee.gtao_factor=1.1;sc.eevee.taa_render_samples=128
sc.view_settings.view_transform='Standard';sc.view_settings.look='Medium High Contrast';sc.view_settings.exposure=-.3
sc.render.resolution_x=1600;sc.render.resolution_y=1200;sc.render.resolution_percentage=100;sc.render.image_settings.file_format='PNG'
sc.camera=cams[0]
clay=plume
for material in bird.data.materials:
 if material!=black:
  material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.26,.28,.30,1)
  material.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.8
bpy.ops.object.select_all(action='DESELECT');bird.select_set(True);bpy.context.view_layer.objects.active=bird
for screen in bpy.data.screens:
 for area in screen.areas:
  if area.type=='VIEW_3D':
   a=area.spaces.active;a.clip_start=.001;a.region_3d.view_distance=.8;a.region_3d.view_location=(0,.025,.17);a.shading.type='MATERIAL';a.overlay.show_extras=False

bpy.ops.wm.save_as_mainfile(filepath=str(R/'Kestrel_Model_v03.blend'))

stats={'scope':'Model only; no animation','vertices':len(bird.data.vertices),'triangles':sum(len(p.vertices)-2 for p in bird.data.polygons),'materials':len(bird.data.materials),'shape_keys':[k.name for k in bird.data.shape_keys.key_blocks],'actions':len(bpy.data.actions),'finite_vertices':all(math.isfinite(x) for v in bird.data.vertices for x in v.co)}
(R/'validation.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
for i,c in enumerate(cams):
 bird.data.shape_keys.key_blocks['Folded_Wings_Review'].value=1 if i>=3 else 0
 sc.camera=c;sc.render.filepath=str(R/'Previews'/(c.name+'.png'));bpy.ops.render.render(write_still=True)
print('MODEL_V03_OK',json.dumps(stats))
