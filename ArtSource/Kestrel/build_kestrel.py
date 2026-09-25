"""Reproducible structural kestrel study. Blender 3.6, run with --background --python.
Original procedural geometry/textures; photographs are visual references only.
Not a finished production character: see README for remaining art validation.
"""
import bpy, math, random, json
import numpy as np
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
random.seed(29)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
mesh_objects = []
bone_specs = {}

def material(name, color, roughness=.65):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Roughness'].default_value = roughness
    p.inputs['Specular'].default_value = .25
    return m

def feather_material(name, base, dark, mode):
    # UV-based pattern survives FBX export as a regular PNG texture.
    size = 1024
    y, x = np.mgrid[0:size, 0:size].astype(np.float32) / (size-1)
    rng = np.random.default_rng(21)
    grain = rng.normal(0, .018, (size, size))
    shaft_distance = np.abs(x-.5)
    barb_phase = y*230 + shaft_distance*82
    fine = .025*np.sin(barb_phase*math.tau) + grain
    if mode == 'bar':
        phase = y*6.8 + .10*np.cos(x*math.tau) + .035*np.sin(x*33)
        mask = np.clip((np.cos(phase*math.tau)-.26)*7, 0, 1)
        mask = np.maximum(mask, ((y>.85)&(y<.94))*.95)
    elif mode == 'streak':
        mask = np.clip((.20-shaft_distance)*22, 0, 1)*np.clip((y-.25)*5, 0, 1)
        mask *= np.clip((.94-y)*12, 0, 1)
    elif mode == 'back':
        mask = np.clip((np.cos((y*2.8+shaft_distance*.25)*math.tau)-.15)*8, 0, 1)
    else:
        mask = np.zeros_like(x)
    border = np.clip((shaft_distance-.47)*4, 0, .12) + np.clip((y-.985)*8, 0, .12)
    col = np.array(base)[None,None,:]*(1-mask[:,:,None]) + np.array(dark)[None,None,:]*mask[:,:,None]
    col = col*(1-border[:,:,None]) + np.array((.59,.44,.27))[None,None,:]*border[:,:,None]
    col += fine[:,:,None]
    shaft = np.clip((.008-shaft_distance)*100, 0, .5)
    col = col*(1-shaft[:,:,None]) + np.array((.25,.18,.105))*shaft[:,:,None]
    rgba = np.ones((size,size,4), dtype=np.float32)
    rgba[:,:,:3] = np.clip(col, .008, .95)
    img = bpy.data.images.new(name+'_BaseColor', width=size, height=size)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = str(ROOT/'Textures'/(name+'_BaseColor.png'))
    img.file_format = 'PNG'
    img.save()
    m = material(name, base)
    ns=m.node_tree.nodes; lk=m.node_tree.links; p=ns.get('Principled BSDF')
    tex=ns.new('ShaderNodeTexImage'); tex.image=img
    lk.new(tex.outputs['Color'],p.inputs['Base Color'])
    bump=ns.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=.12; bump.inputs['Distance'].default_value=.00012
    lk.new(tex.outputs['Color'],bump.inputs['Height']); lk.new(bump.outputs['Normal'],p.inputs['Normal'])
    return m

back=feather_material('Kestrel_RufousBarred',(.36,.19,.085),(.038,.026,.019),'back')
flight=feather_material('Kestrel_FlightBarred',(.37,.28,.17),(.047,.038,.029),'bar')
tailmat=feather_material('Kestrel_TailBarred',(.46,.30,.15),(.045,.031,.023),'bar')
breast=feather_material('Kestrel_BreastStreaks',(.66,.52,.34),(.075,.044,.026),'streak')
headmat=feather_material('Kestrel_CrownStreaks',(.34,.235,.14),(.065,.045,.03),'streak')
cream=material('Kestrel_Throat',(.58,.49,.36))
under=material('Kestrel_Underplumage',(.22,.14,.077))
dark=material('Kestrel_MalarStripe',(.046,.033,.024))
yellow=material('Kestrel_CereAndFeet',(.65,.38,.035),.46)
beakmat=material('Kestrel_Horn',(.045,.065,.075),.32)
eye=material('Kestrel_Eye',(.006,.009,.011),.095)
clawmat=material('Kestrel_Claws',(.045,.032,.022),.30)

def bone(name, head, tail, parent=None, deform=True):
    bone_specs[name]=(Vector(head),Vector(tail),parent,deform)

bone('ROOT',(0,0,0),(0,0,.04),None,False)
bone('Body',(0,.038,.17),(0,-.027,.205),'ROOT')
bone('Neck',(0,-.027,.205),(0,-.066,.232),'Body')
bone('Head',(0,-.066,.232),(0,-.112,.247),'Neck')
bone('Jaw',(0,-.104,.23),(0,-.135,.225),'Head')
bone('Tail',(0,.055,.175),(0,.112,.158),'Body')

def mesh(name, verts, faces, mat, weights, uvs=None):
    data=bpy.data.meshes.new(name)
    data.from_pydata(verts,[],faces); data.update()
    obj=bpy.data.objects.new(name,data); scene.collection.objects.link(obj)
    data.materials.append(mat)
    for p in data.polygons:p.use_smooth=True
    if uvs:
        layer=data.uv_layers.new(name='UVMap')
        for poly in data.polygons:
            for li in poly.loop_indices:layer.data[li].uv=uvs[data.loops[li].vertex_index]
    for bn, entries in weights.items():
        vg=obj.vertex_groups.new(name=bn)
        for vi,w in entries:vg.add([vi],w,'REPLACE')
    mesh_objects.append(obj)
    return obj

def ellipsoid(name,center,scale,mat,bn,segments=32,rings=20):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments,ring_count=rings,location=center)
    o=bpy.context.object; o.name=name; o.scale=scale
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    o.data.materials.append(mat)
    for p in o.data.polygons:p.use_smooth=True
    vg=o.vertex_groups.new(name=bn);vg.add(list(range(len(o.data.vertices))),1,'REPLACE')
    mesh_objects.append(o)
    return o

def tube(name,points,radii,mat,bn,sides=10):
    verts=[];uv=[];faces=[]
    for i,p in enumerate(points):
        p=Vector(p)
        tangent=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])
        tangent.normalize(); a=tangent.cross(Vector((0,0,1)))
        if a.length<.001:a=tangent.cross(Vector((1,0,0)))
        a.normalize();b=tangent.cross(a).normalized()
        for j in range(sides):
            ang=j*math.tau/sides
            verts.append(tuple(p+radii[i]*(a*math.cos(ang)+b*math.sin(ang))))
            uv.append((j/sides,i/(len(points)-1)))
            if i:
                q=i*sides+j; prev=i*sides+(j+1)%sides
                faces.append((q,prev,prev-sides,q-sides))
    faces.append(tuple(reversed(range(sides))))
    faces.append(tuple(range((len(points)-1)*sides,len(points)*sides)))
    return mesh(name,verts,faces,mat,{bn:[(i,1) for i in range(len(verts))]},uv)

def feather(name,start,end,width,mat,bn,normal=(0,0,1),segments=12,asym=.48):
    a=Vector(start);d=Vector(end)-a;n=Vector(normal).normalized()
    side=d.cross(n).normalized()
    if side.length<.1:side=Vector((1,0,0))
    verts=[];uv=[];faces=[]
    for i in range(segments+1):
        t=i/segments
        profile=(math.sin(math.pi*(.06+.94*t))**.46) * (.84+.16*(1-t))
        if i==segments:profile=.012
        mid=a+d*t+n*(math.sin(math.pi*t)*width*.035)
        for j,u in enumerate((-1,-.5,0,.5,1)):
            w=width*(asym if u<0 else 1-asym)
            pos=mid+side*u*w-n*abs(u)*width*.025
            verts.append(tuple(pos));uv.append(((u+1)/2,t))
            if i and j: k=i*5+j;faces.append((k-6,k-5,k,k-1))
    return mesh(name,verts,faces,mat,{bn:[(i,1) for i in range(len(verts))]},uv)

# Unified ring-built torso; longitudinal skin weighting continues through neck.
rings=[(.09,.174,.003,.008),(.074,.181,.019,.022),(.045,.187,.03,.034),(.008,.195,.037,.039),(-.026,.204,.034,.037),(-.049,.218,.024,.029),(-.066,.234,.018,.019),(-.079,.241,.012,.012)]
vs=[];fs=[];uv=[];weights={'Body':[],'Neck':[],'Head':[]}
for i,(y,z,rx,rz) in enumerate(rings):
    for j in range(48):
        t=j*math.tau/48;vs.append((rx*math.cos(t),y,z+rz*math.sin(t)));uv.append((j/47,i/(len(rings)-1)))
        nw=max(0,min(1,(-y-.02)/.05));hw=max(0,min(1,(-y-.057)/.026))
        for bn,w in [('Body',1-nw),('Neck',nw*(1-hw)),('Head',nw*hw)]:
            if w:weights[bn].append((len(vs)-1,w))
        if i:
            k=i*48+j;jn=i*48+(j+1)%48;fs.append((k-48,jn-48,jn,k))
fs.extend([tuple(reversed(range(48))),tuple(range((len(rings)-1)*48,len(rings)*48))])
mesh('Torso',vs,fs,under,weights,uv)
ellipsoid('Head_Form',(0,-.08,.241),(.028,.033,.030),headmat,'Head')
ellipsoid('Throat',(0,-.089,.222),(.019,.022,.019),cream,'Head')

# Contour feathers follow the body; short overlapping rows preserve bird silhouette.
for i in range(22):
    y=-.052+i*.006
    # Interpolated body radii at this longitudinal section.
    rr=sorted(rings)
    for q in range(len(rr)-1):
        if rr[q][0]<=y<=rr[q+1][0]:
            u=(y-rr[q][0])/(rr[q+1][0]-rr[q][0]);_,z,rx,rz=[rr[q][k]*(1-u)+rr[q+1][k]*u for k in range(4)];break
    for j in range(36):
        a=(j+(i%2)*.5)*math.tau/36
        n=Vector((math.cos(a),0,math.sin(a)))
        start=Vector(((rx+.001)*math.cos(a),y,z+(rz+.001)*math.sin(a)))
        yy=min(.085,y+.012)
        for q in range(len(rr)-1):
            if rr[q][0]<=yy<=rr[q+1][0]:
                u=(yy-rr[q][0])/(rr[q+1][0]-rr[q][0]);_,zz,rxx,rzz=[rr[q][k]*(1-u)+rr[q+1][k]*u for k in range(4)];break
        end=Vector(((rxx+.0015)*math.cos(a),yy,zz+(rzz+.0015)*math.sin(a)))
        bn='Neck' if y<-.037 else 'Body'
        m=back if math.sin(a)>.22 else breast
        feather('Contour_%02d_%02d'%(i,j),start,end,.008,m,bn,n,6)

# Fine crown plumage, cheek stripe, yellow orbital skin and eyes.
for s in (-1,1):
    ellipsoid('Malar_'+str(s),(s*.0205,-.095,.229),(.002,.007,.010),dark,'Head',20,12)
    ellipsoid('Orbital_'+str(s),(s*.0247,-.094,.247),(.003,.008,.008),yellow,'Head',32,16)
    ellipsoid('Eye_'+str(s),(s*.0263,-.095,.2478),(.0032,.0068,.0068),eye,'Head',32,20)
for i in range(19):
    theta=.20+i*.145
    for j in range(36):
        phi=j*math.tau/36+(i%2)*.08
        # Surface-following feather patches around the head, leaving eyes exposed.
        def hp(t):return Vector((.0282*math.sin(t)*math.cos(phi),-.08-.0332*math.cos(t),.241+.0302*math.sin(t)*math.sin(phi)))
        a=hp(theta);e=hp(min(theta+.23,3.10))
        if abs(a.x)>.019 and a.y<-.081 and .238<a.z<.256:continue
        n=Vector((a.x/.0282,(a.y+.08)/.0332,(a.z-.241)/.0302)).normalized()
        feather('HeadPlumage_%s_%s'%(i,j),a,e,.0055,headmat if a.z>.24 else breast,'Head',n,5)
ellipsoid('Cere',(0,-.111,.239),(.013,.014,.010),yellow,'Head',28,16)
tube('Upper_Beak',[(0,-.119,.24),(0,-.131,.238),(0,-.14,.231),(0,-.1405,.22)],[.009,.007,.004,.0003],beakmat,'Head',16)
tube('Lower_Beak',[(0,-.116,.23),(0,-.131,.226),(0,-.138,.224)],[.007,.004,.0005],beakmat,'Jaw',12)
for s in (-1,1):ellipsoid('Nostril_'+str(s),(s*.0095,-.119,.241),(.001,.0023,.0014),dark,'Head',16,8)

for s,suffix in [(1,'L'),(-1,'R')]:
    shoulder=(s*.027,-.024,.214);elbow=(s*.113,.011,.21);wrist=(s*.211,-.026,.211);hand=(s*.252,-.031,.208)
    upper='WingUpper.'+suffix;fore='WingFore.'+suffix;handbn='WingHand.'+suffix
    bone(upper,shoulder,elbow,'Body');bone(fore,elbow,wrist,upper);bone(handbn,wrist,hand,fore)
    tube('WingLeading_'+suffix,[shoulder,elbow,wrist,hand],[.009,.006,.004,.002],under,upper,12)
    # Reassign wing leading-edge skin by segment to avoid a rigid elbow.
    obj=mesh_objects[-1];obj.vertex_groups.clear()
    for b,ids in [(upper,range(0,18)),(fore,range(18,36)),(handbn,range(36,48))]:
        obj.vertex_groups.new(name=b).add(list(ids),1,'REPLACE')
    for i in range(10):
        t=i/9
        a=Vector(wrist).lerp(Vector(hand),t)
        e=Vector((s*(.222+.148*t),.125-.145*t,.201-.013*t))
        bn='Primary_%02d.%s'%(i+1,suffix);bone(bn,a,e,handbn)
        feather(bn,a,e,.029-.009*t,flight,bn,segments=16,asym=.36)
    for i in range(9):
        t=i/8
        a=Vector(elbow).lerp(Vector(wrist),t)
        e=a+Vector((s*.016,.112-.012*t,-.01))
        bn='Secondary_%02d.%s'%(i+1,suffix);bone(bn,a,e,fore)
        feather(bn,a,e,.025,flight,bn,segments=14)
    for i in range(5):
        a=Vector(shoulder).lerp(Vector(elbow),i/5)
        e=a+Vector((s*.018,.085,-.008))
        feather('Tertial_%s_%s'%(i,suffix),a,e,.029,back,upper,segments=12)
    # Coverts: ordered short rows on both upper wing and underside.
    for row in range(4):
        for i in range(26):
            t=i/25
            if t<.40:a=Vector(shoulder).lerp(Vector(elbow),t/.40);bn=upper
            elif t<.84:a=Vector(elbow).lerp(Vector(wrist),(t-.40)/.44);bn=fore
            else:a=Vector(wrist).lerp(Vector(hand),(t-.84)/.16);bn=handbn
            a+=Vector((0,-.005+.013*row,.008-row*.0015))
            e=a+Vector((s*.007,.035+row*.009,-.001))
            feather('Covert_%s_%s_%s'%(suffix,row,i),a,e,.015+row*.002,back,bn,segments=7)
            a.z-=.014;e.z-=.014
            feather('Underwing_%s_%s_%s'%(suffix,row,i),a,e,.014+row*.002,breast,bn,(0,0,-1),6)
    hip=(s*.02,.012,.172);ankle=(s*.024,.02,.112);foot=(s*.025,-.007,.065)
    thigh='Thigh.'+suffix;shin='Shin.'+suffix;footbn='Foot.'+suffix
    bone(thigh,hip,ankle,'Body');bone(shin,ankle,foot,thigh);bone(footbn,foot,(s*.025,-.02,.052),shin)
    ellipsoid('LegPlumage_'+suffix,(s*.022,.017,.146),(.014,.02,.038),breast,thigh,24,16)
    tube('Tarsus_'+suffix,[ankle,(s*.025,.012,.09),foot],[.0048,.004,.0045],yellow,shin)
    for ti in range(4):
        dx=(ti-1)*.011 if ti<3 else -.006*s
        end=(s*.025+dx,-.038 if ti<3 else .024,.050)
        mid=(s*.025+dx*.5,(-.007+end[1])*.5,.051)
        bn='Toe_%s.%s'%(ti+1,suffix);tipbn='ToeTip_%s.%s'%(ti+1,suffix)
        bone(bn,foot,mid,footbn);bone(tipbn,mid,end,bn)
        tube(bn,[foot,mid],[.0028,.0022],yellow,bn)
        tube(tipbn,[mid,end],[.0022,.0017],yellow,tipbn)
        sign=-1 if ti<3 else 1
        tube('Claw_'+tipbn,[end,(end[0],end[1]+sign*.005,.049),(end[0],end[1]+sign*.007,.044)],[.0019,.0011,.00015],clawmat,tipbn,8)
        for k in range(1,5):
            p=Vector(mid).lerp(Vector(end),k/5);p.z+=.0018
            ellipsoid('ToeScale',p,(.0022,.0011,.0006),yellow,tipbn,8,4)

for i in range(12):
    t=(i-5.5)/5.5
    a=(t*.013,.064,.18-abs(t)*.002)
    e=(t*.048,.226-t*t*.012,.147)
    bn='TailFeather_%02d'%(i+1);bone(bn,a,e,'Tail')
    feather(bn,a,e,.020,tailmat,bn,segments=18)
for i in range(7):
    x=(i-3)*.006
    feather('Rump_'+str(i),(x,.043,.207),(x*1.2,.11,.179),.018,back,'Body',segments=10)

# Build deformation rig. All long feathers are individually accessible.
armdata=bpy.data.armatures.new('Kestrel_Skeleton')
rig=bpy.data.objects.new('Kestrel_Rig',armdata);scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig;rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
for name,(a,b,parent,deform) in bone_specs.items():
    eb=armdata.edit_bones.new(name);eb.head=a;eb.tail=b;eb.use_deform=deform
    if parent:eb.parent=armdata.edit_bones[parent]
    eb.align_roll(Vector((0,0,1)))
bpy.ops.object.mode_set(mode='OBJECT')
rig.show_in_front=True;armdata.display_type='STICK'
rig['stage']='Structural study; four production animations pending'
rig['appearance']='Brown crown, barred back and tail'
for pb in rig.pose.bones:pb.rotation_mode='XYZ'

# Join model to a single skinned object, retaining named material slots and weights.
bpy.ops.object.select_all(action='DESELECT')
for o in mesh_objects:o.select_set(True)
bpy.context.view_layer.objects.active=mesh_objects[0]
bpy.ops.object.join()
bird=bpy.context.object;bird.name='Kestrel_SkinnedMesh'
mod=bird.modifiers.new('Kestrel_Skin','ARMATURE');mod.object=rig
bird.parent=rig
bird['stage']='Art blockout with procedural reference plumage; not final close-up textures'

# Weight and mesh checks before saving any deliverable.
missing=[];bad=[]
for v in bird.data.vertices:
    if not v.groups:missing.append(v.index)
    if abs(sum(g.weight for g in v.groups)-1)>.001:bad.append(v.index)
assert not missing and not bad,(len(missing),len(bad))
assert all(g.name in armdata.bones for g in bird.vertex_groups)
assert all(math.isfinite(c) for v in bird.data.vertices for c in v.co)

# Neutral studio review cameras. These are not exported with the character.
studio=bpy.data.collections.new('Review_Studio');scene.collection.children.link(studio)
def move_to_studio(o):
    for c in list(o.users_collection):c.objects.unlink(o)
    studio.objects.link(o)
def camera(name,pos,target,scale):
    bpy.ops.object.camera_add(location=pos);c=bpy.context.object;c.name=name
    c.rotation_euler=(Vector(target)-c.location).to_track_quat('-Z','Y').to_euler()
    c.data.type='ORTHO';c.data.ortho_scale=scale;c.data.lens=55;move_to_studio(c);return c
cams=[camera('Review_ThreeQuarter',(.57,-.78,.70),(0,.035,.17),.91),camera('Review_Dorsal',(0,.04,1.3),(0,.04,.17),.86),camera('Review_Head',(.36,-.48,.34),(0,-.063,.23),.20)]
for name,pos,power,size in [('Key',(.1,-.6,1),14,.7),('Fill',(-.65,-.2,.5),8,.55),('Rim',(.1,.65,.7),14,.5)]:
    bpy.ops.object.light_add(type='AREA',location=pos);o=bpy.context.object;o.name=name;o.data.energy=power;o.data.shape='DISK';o.data.size=size
    o.rotation_euler=(Vector((0,0,.15))-o.location).to_track_quat('-Z','Y').to_euler();move_to_studio(o)
world=bpy.data.worlds.new('Studio_World');scene.world=world;world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.065,.078,.09,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.5
scene.render.engine='BLENDER_EEVEE';scene.eevee.use_gtao=True;scene.eevee.gtao_distance=.025;scene.eevee.gtao_factor=1.15;scene.eevee.taa_render_samples=96
scene.view_settings.view_transform='Standard';scene.view_settings.look='Medium High Contrast';scene.view_settings.exposure=-.6
scene.render.resolution_x=1400;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.camera=cams[0]
scene.render.film_transparent=False
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_distance=.95
            area.spaces.active.region_3d.view_location=(0,.025,.17)
            area.spaces.active.clip_start=.001
            area.spaces.active.shading.type='MATERIAL'
            area.spaces.active.overlay.show_extras=False
bpy.ops.object.select_all(action='DESELECT');bird.select_set(True);bpy.context.view_layer.objects.active=bird
for image in bpy.data.images:
    if image.source=='FILE':image.pack()
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Kestrel_Structure_v01.blend'))

bpy.ops.object.select_all(action='DESELECT');bird.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(ROOT/'Export'/'Kestrel_Structure_v01.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,path_mode='COPY',embed_textures=True)
stats={'stage':'structural model and deformation skeleton; no production animations','vertices':len(bird.data.vertices),'triangles':sum(len(p.vertices)-2 for p in bird.data.polygons),'bones':len(armdata.bones),'materials':len(bird.data.materials),'unweighted_vertices':len(missing),'invalid_weight_sums':len(bad),'dimensions_m':list(bird.dimensions),'animations':[]}
(ROOT/'validation.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
for c in cams:
    scene.camera=c;scene.render.filepath=str(ROOT/'Previews'/(c.name+'.png'));bpy.ops.render.render(write_still=True)
print('KESTREL_BUILD_OK',json.dumps(stats))
