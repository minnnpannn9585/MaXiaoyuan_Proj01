"""One-time source migration from v02; no texture or image modifications."""
from pathlib import Path
p=Path(__file__).with_name('build_clay.py')
s=p.read_text(encoding='utf-8')
start=s.index("plume=mat(");end=s.index("horn=mat(")
s=s[:start]+"plume=mat('Neutral_Clay',(.36,.38,.40),.85)\n"+s[end:]
s=s.replace("from mathutils import Vector","from mathutils import Vector\nfrom mathutils.bvhtree import BVHTree")
start=s.index('# Continuous smooth body');end=s.index('# Eye sockets')
s=s[:start]+'''# Longitudinal anatomical sections: one continuous envelope, no fused balls.
# y, center-height, lateral half-width, dorsal radius, ventral radius
sections=[(-.112,.226,.003,.003,.004),(-.106,.229,.013,.012,.009),
 (-.096,.232,.021,.018,.017),(-.082,.232,.024,.021,.023),
 (-.068,.224,.022,.024,.025),(-.054,.207,.022,.025,.028),
 (-.037,.190,.028,.026,.032),(-.015,.181,.033,.032,.037),
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
   k=i*sides+j;n=i*sides+(j+1)%sides;fs.append((k-sides,k,n,n-sides))
fs.append(tuple(reversed(range(sides))));fs.append(tuple(range(len(vs)-sides,len(vs))))
body=mesh('Anatomical_Body',vs,fs,uvs)
bvh=BVHTree.FromPolygons([v.co for v in body.data.vertices],[list(p.vertices) for p in body.data.polygons])

'''+s[end:]
start=s.index('# Eye sockets');end=s.index('def feather_geometry')
s=s[:start]+'''# Eye placement follows the actual skull surface rather than floating spheres.
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

'''+s[end:]
# More compact leg silhouette, especially the exposed tarsus.
s=s.replace("hip=(s*.018,.008,.150);ankle=(s*.023,.025,.115);foot=(s*.023,.003,.065)","hip=(s*.018,.008,.150);ankle=(s*.021,.029,.128);foot=(s*.021,.004,.096)")
s=s.replace("(s*.020,.017,.138),(.012,.018,.029)","(s*.019,.016,.144),(.010,.020,.020)")
s=s.replace("(s*.024,.017,.087)","(s*.021,.018,.109)").replace(".025,.055)",".025,.087)").replace("mid.z=.056","mid.z=.088")
s=s.replace("(s*.023+dx,","(s*.021+dx,")
s=s.replace("for k in range(1,7):","for k in range(0):").replace("for k in range(9):","for k in range(0):")
# Fold the long wing along the flank. Static review only.
s=s.replace("foldshoulder=Vector((s*.030,-.019,.197));foldelbow=Vector((s*.038,.030,.190));foldwrist=Vector((s*.039,-.005,.185));foldhand=Vector((s*.037,.047,.178))","foldshoulder=Vector((s*.023,-.033,.202));foldelbow=Vector((s*.034,-.002,.187));foldwrist=Vector((s*.031,.039,.176));foldhand=Vector((s*.021,.077,.162))")
s=s.replace("(s*(.039+.0007*i),.027+.002*i,.181-.0009*i)","(s*(.033-.0006*i),.032+.003*i,.184-.0016*i)")
s=s.replace("(s*(.024+.0018*i),.164+.002*i,.153-.0015*i)","(s*(.016+.0007*i),.139+.003*i,.146-.0015*i)")
s=s.replace("(s*(.040+.002*t),.122-.018*t,.164+.009*t)","(s*(.029-.005*t),.097+.026*t,.151-.006*t)")
s=s.replace(".071,-.013",".062,-.025").replace("(s*.003,.040,-.007)","(s*-.003,.040,-.018)")
s=s.replace("s*(.008+row*.002)","s*(.003+row*.001)")
# Make the outer primaries less equally spaced, and the leading edge less flat.
s=s.replace("(.22+.142*t),.115-.148*t,.184-.006*t","(.223+.128*math.sin(t*math.pi*.62)),.106-.12*t,.184+.014*t*t")
# Perched model pitches torso up; feet retain their support position.
start=s.index('# Modeling fold target;')
s=s[:start]+'''# A static perch silhouette: torso rises and tail drops, with legs attached at hips.
from mathutils import Matrix
perch=Matrix.Rotation(math.radians(-32),3,'X');pivot=Vector((0,.018,.145))
for ob in parts:
 if ob.name.startswith(('Toe_','Talon_','Tarsus.')):continue
 fold_positions[ob.as_pointer()]=[tuple(pivot+perch@(Vector(v)-pivot)) for v in fold_positions[ob.as_pointer()]]

'''+s[start:]
s=s.replace('v02','v03').replace("img.pack()","")
s=s.replace("bpy.ops.export_scene.fbx(filepath=str(R/'Export/Kestrel_Model_v03.fbx'),use_selection=True,object_types={'MESH'},use_mesh_modifiers=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='COPY',embed_textures=True)","")
s=s.replace(",'atlas_size':list(img.size)","")
s=s.replace("bird['Texture']='Original image_gen plumage atlas; prompt in Texture_Prompt.md'","bird['Texture']='None; geometry assessment only'")
s=s.replace("sc.camera=cams[0]","sc.camera=cams[0]\nclay=plume\nsc.view_layers[0].material_override=clay")
s=s.replace(".65),('Soft_Fill'",".65),('Soft_Fill'")
s=s.replace("cam('Folded_Side',(.8,.0,.24),(0,.035,.15),.42)","cam('Folded_Side',(.8,.0,.24),(0,.01,.17),.38)")
p.write_text(s,encoding='utf-8')
