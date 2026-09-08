"""Build the two original RTS structure kits. Run with Blender --background --python.

No external textures/add-ons. Every FBX is one named mesh at ground origin,
with stable faction material slots, applied bevels, and explicit meter scale.
"""
import bpy, bmesh, math, os, json, sys
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '../..'))
OUT = os.path.join(ROOT, 'Assets/Armies/Models/Structures')
SRC = os.path.join(ROOT, 'ArtSource/Structures')
os.makedirs(OUT, exist_ok=True)
os.makedirs(SRC, exist_ok=True)
FACTIONS = ['Vanguard', 'Dynasty']
IDS = ['Command', 'Power', 'Refinery', 'Barracks', 'Factory', 'Airfield', 'Tech', 'Turret', 'AirDefense', 'Superweapon']
parts = []
mat = {}
fac = ''

def material(name, color, metallic=0.0, roughness=.45, emission=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Metallic'].default_value = metallic
    p.inputs['Roughness'].default_value = roughness
    if emission:
        p.inputs['Emission Color'].default_value = (*color, 1)
        p.inputs['Emission Strength'].default_value = emission
    return m

def palette(f):
    if f == 'Vanguard':
        colors = {'Armor':(.62,.56,.40), 'Secondary':(.27,.31,.24), 'Glow':(.20,.36,.55), 'Metal':(.15,.16,.14), 'Glass':(.06,.115,.13), 'Accent':(.65,.56,.30)}
    else:
        colors = {'Armor':(.32,.38,.22), 'Secondary':(.36,.20,.13), 'Glow':(.65,.14,.08), 'Metal':(.15,.16,.12), 'Glass':(.07,.105,.09), 'Accent':(.66,.52,.24)}
    return {k: material(f+'_'+k, v, .50 if k=='Metal' else .04, .23 if k=='Glass' else .80, 0) for k,v in colors.items()}
def finish(o, name, material_key, bevel=0):
    o.name=name
    o.data.materials.append(mat[material_key])
    if bevel:
        m=o.modifiers.new('Fabricated softened edges','BEVEL'); m.width=bevel; m.segments=1
        bpy.context.view_layer.objects.active=o
        bpy.ops.object.modifier_apply(modifier=m.name)
    parts.append(o)
    return o

def box(name, pos, dims, material_key='Armor', bevel=.10, rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    o=bpy.context.object; o.dimensions=dims
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if rot: o.rotation_euler=rot
    return finish(o, name, material_key, min(bevel, min(dims)*.22))

def cyl(name, pos, radius, height, material_key='Metal', vertices=12, rot=None, radius_top=None):
    if radius_top is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=height, location=pos)
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius, radius2=radius_top, depth=height, location=pos)
    o=bpy.context.object
    if rot: o.rotation_euler=rot
    return finish(o,name,material_key,.04 if radius>.15 else 0)

def beam(name, a, b, radius=.12, material_key='Metal', vertices=8):
    delta=Vector(b)-Vector(a)
    o=cyl(name,(Vector(a)+Vector(b))*.5,radius,delta.length,material_key,vertices)
    o.rotation_euler=delta.to_track_quat('Z','Y').to_euler()
    return o

def slab(name, pos, bottom, top, h, material_key='Armor'):
    # Rectangular chamfered frustum. Broad roof overhangs on Dynasty form pagoda-like tiers.
    x,y,z=pos
    def perimeter(w,d):
        c=min(w,d)*.14
        return [(-w/2+c,-d/2),(w/2-c,-d/2),(w/2,-d/2+c),(w/2,d/2-c),(w/2-c,d/2),(-w/2+c,d/2),(-w/2,d/2-c),(-w/2,-d/2+c)]
    vs=[(x+a,y+b,z-h/2) for a,b in perimeter(*bottom)]+[(x+a,y+b,z+h/2) for a,b in perimeter(*top)]
    fs=[tuple(reversed(range(8))),tuple(range(8,16))]+[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(vs,[],fs); mesh.update()
    o=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(o)
    return finish(o,name,material_key)

def roof(name, x,y,z,w,d):
    # Fabricated corrugated roofs and a broad ridge create grounded base silhouettes.
    rise=min(w*.14,.82)
    pitch=math.atan2(rise,w/2)
    run=math.sqrt((w/2+.20)**2+rise**2)
    for side in [-1,1]:
        box(name+' sloping roof sheet',(x+side*w*.255,y,z+rise*.5),(run,d+.44,.13),'Secondary',.018,(0,side*pitch,0))
        for i in range(max(4,int(d/.48))):
            yy=y-d/2+i*d/(max(4,int(d/.48))-1)
            beam(name+' raised roof seam',(x+side*(w/2+.17),yy,z+.11),(x,yy,z+rise+.14),.030,'Metal',6)
        box(name+' rain gutter',(x+side*(w/2+.16),y,z-.09),(.16,d+.5,.14),'Metal',.018)
    box(name+' roof ridge',(x,y,z+rise+.07),(.22,d+.51,.12),'Armor',.018)
def base(w,d):
    slab('Foundation perimeter',(0,0,.24),(w,d),(w-.2,d-.2),.48,'Metal')
    slab('Raised service deck',(0,0,.48),(w-.22,d-.22),(w-.5,d-.5),.18,'Accent' if fac=='Dynasty' else 'Secondary')
    for x in [-w/2+.6,w/2-.6]:
        for y in [-d/2+.6,d/2-.6]:
            box('Corner armored footing',(x,y,.75),(1.05,1.05,.66),'Secondary',.13)
            box('Corner status light',(x,y-.54,.81),(.58,.04,.11),'Glow',.01)

def body(name,x,y,z,w,d,h):
    slab(name,(x,y,z),(w,d),(w-.45,d-.35),h,'Armor')
    box(name+' front shadow reveal',(x,y-d/2-.025,z-h/2+.33),(w-.35,.08,.20),'Metal',.01)
    for sx in [-1,1]:
        box(name+' structural shoulder',(x+sx*(w/2-.22),y,z),(.48,d+.12,h-.1),'Secondary',.10)
    roof(name,x,y,z+h/2+.11,w,d)

def door(x,y,z,w=2.2,h=2.3):
    box('Recessed entrance',(x,y,z+h/2),(w+.48,.18,h+.35),'Metal',.10)
    for s in [-1,1]:
        box('Entry split blast door',(x+s*w/4,y-.10,z+h/2),(w/2-.05,.11,h),'Secondary',.06)
        box('Entry vertical light',(x+s*(w/2+.15),y-.12,z+h/2),(.075,.06,h-.13),'Glow',.01)
    box('Entry glowing lintel',(x,y-.14,z+h+.11),(w+.35,.06,.09),'Glow',.01)
    for i in range(4):
        box('Entry stair',(x,y-.18-i*.32,z-.08-i*.11),(w+.55,.65,.22),'Metal',.03)

def windows(x,y,z,w,count=4,height=.58):
    for i in range(count):
        px=x+(i-(count-1)/2)*(w/count)
        box('Armored window recess',(px,y,z),(w/count-.13,.10,height+.14),'Metal',.025)
        box('Tinted window',(px,y-.056,z),(w/count-.24,.04,height),'Glass',.02)
        box('Window luminous lower edge',(px,y-.082,z-height/2+.03),(w/count-.25,.025,.04),'Glow',.008)

def vents(x,y,z,w=1.2,h=1.5):
    box('Vent recess',(x,y,z),(w,.12,h),'Metal',.03)
    for i in range(5):
        box('Vent louver',(x,y-.10,z-h*.38+i*h*.19),(w-.12,.12,.07),'Accent',.01)

def beacon(x,y,z,height=2.0):
    cyl('Antenna base',(x,y,z+.13),.27,.26,'Secondary')
    beam('Communications mast',(x,y,z),(x,y,z+height),.07,'Metal')
    cyl('Antenna beacon',(x,y,z+height),.105,.25,'Glow',8)
    beam('Antenna cross arm',(x-.55,y,z+height*.7),(x+.55,y,z+height*.7),.04,'Accent')

def tank(x,y,z,r=1.25,h=3):
    cyl('Tank support',(x,y,z+.16),r+.18,.32,'Secondary',16)
    cyl('Storage vessel',(x,y,z+.32+h/2),r,h,'Armor',16)
    cyl('Tank domed cap',(x,y,z+.32+h+.20),r,.4,'Secondary',16,radius_top=r*.62)
    for zh in [.60,h-.10]:
        cyl('Vessel luminous band',(x,y,z+zh),r+.035,.10,'Glow',16)
    for a in range(0,360,90):
        rr=math.radians(a)
        box('Tank armored rib',(x+math.cos(rr)*(r+.03),y+math.sin(rr)*(r+.03),z+.32+h/2),(.14,.14,h),'Metal',.025)

def dish(x,y,z,r=1.5):
    cyl('Dish rotating pedestal',(x,y,z-.5),.5,1.0,'Secondary',12)
    # Upward-open radar bowl, with visible interior and cyan focal feed.
    n=20; vertices=[]
    for rad,zh in [(r*.18,0),(r,.52),(r,.61),(r*.18,.15)]:
        vertices.extend([(x+math.cos(i*2*math.pi/n)*rad,y+math.sin(i*2*math.pi/n)*rad,z+zh) for i in range(n)])
    faces=[]
    for k in range(4):
        for i in range(n): faces.append((k*n+i,k*n+(i+1)%n,((k+1)%4)*n+(i+1)%n,((k+1)%4)*n+i))
    me=bpy.data.meshes.new('Radar bowl'); me.from_pydata(vertices,[],faces); me.update()
    ob=bpy.data.objects.new('Radar bowl',me); bpy.context.collection.objects.link(ob); finish(ob,'Research radar dish','Armor')
    for a in [0,120,240]:
        ar=math.radians(a)
        beam('Dish support strut',(x+math.cos(ar)*r*.8,y+math.sin(ar)*r*.8,z+.5),(x,y,z+1.1),.04,'Accent')
    cyl('Dish signal feed',(x,y,z+1.08),.14,.5,'Glow',8)

def hazard(x,y,z,w=3):
    box('Service hazard backplate',(x,y,z),(w,.055,.24),'Metal',.01)
    for i in range(6):
        box('Diagonal safety marker',(x-w/2+.25+i*(w-.5)/5,y-.034,z),(.13,.03,.22),'Accent',.01,(0,math.radians(-25),0))

def command():
    base(11.5,10)
    body('Command podium',0,.5,1.75,8.8,6.8,2.4)
    for s in [-1,1]:
        body('Operations wing',s*3.8,-1.0,2.1,2.4,5.2,3.1)
        windows(s*3.8,-3.69,2.40,1.8,2,.65)
    door(0,-3.04,.65,2.4,2.1)
    body('Command uplink tower',0,1,5.3,4.0,3.8,4.35)
    box('Executive panoramic glass',(0,-.97,6.45),(3.27,.10,.85),'Glass',.025)
    for z in [4.45,5.48,6.01]:
        box('Tower signal strip',(0,-1.04,z),(2.9,.035,.09),'Glow',.01)
    roof('Observation crown',0,1,7.85,5.2,4.8)
    beacon(-1,1.3,8.13,2.3)
    dish(1.15,1.2,8.28,.8)
    for x in [-2.0,2.0]:
        box('Approach light',(x,-4.25,.66),(.10,1.15,.08),'Glow',.01)

def power():
    base(10,9)
    body('Power turbine hall',0,2.2,1.80,7.4,3.0,2.50)
    door(0,.59,.60,1.6,1.8)
    for x in [-2.28,2.28]:
        # Hyperboloid concrete cooling tower with visible dark mouth and service ribs.
        ys=-1.5; n=32
        levels=[(.58,1.55),(1.02,1.51),(2.1,1.08),(3.35,.91),(4.52,1.07),(5.27,1.31)]
        vertices=[(x+math.cos(i*math.tau/n)*r,ys+math.sin(i*math.tau/n)*r,z) for z,r in levels for i in range(n)]
        faces=[]
        for ring in range(len(levels)-1):
            for i in range(n):faces.append((ring*n+i,ring*n+(i+1)%n,(ring+1)*n+(i+1)%n,(ring+1)*n+i))
        me=bpy.data.meshes.new('Concrete cooling tower shell');me.from_pydata(vertices,[],faces);me.update()
        ob=bpy.data.objects.new('Concrete cooling tower shell',me);bpy.context.collection.objects.link(ob);finish(ob,ob.name,'Armor')
        cyl('Cooling tower dark opening',(x,ys,5.17),1.20,.03,'Metal',32)
        cyl('Cooling tower inner throat',(x,ys,4.98),1.16,.15,'Secondary',32)
        for a in range(0,360,30):
            angle=math.radians(a)
            beam('Cooling tower intake pier',(x+math.cos(angle)*1.46,ys+math.sin(angle)*1.46,.55),(x+math.cos(angle)*1.46,ys+math.sin(angle)*1.46,1.04),.09,'Secondary',8)
        beam('Cooling water trunk',(x,ys,.8),(x,1.4,.8),.24,'Metal',16)
        for y in [-2.1,-1.05]:
            box('Cooling tower service ladder rail',(x+1.34,y,2.53),(.045,.045,3.9),'Metal',.005)
        for z in [1+i*.29 for i in range(12)]:beam('Cooling tower ladder rung',(x+1.34,-2.1,z),(x+1.34,-1.05,z),.024,'Metal',6)
    for x in [-2.2,2.2]:vents(x,.58,1.83,1.4,1.35)
    for x in [-1.1,0,1.1]:
        box('High voltage transformer',(x,2.7,3.43),(.78,1.03,.75),'Metal',.06)
        for i in range(5):box('Transformer cooling fin',(x-.29+i*.145,2.7,3.49),(.052,1.18,.64),'Secondary',.008)
        for y in [2.4,2.9]:cyl('Ceramic transformer insulator',(x,y,4.14),.10,.56,'Armor',12)
    beacon(3.0,2.8,3.5,1.35)
def refinery():
    base(13,11)
    body('Refinery control house',-2.9,1.3,2.2,5.5,5.3,3.35)
    windows(-2.9,-1.44,3.0,4.3,4,.66)
    door(-3.9,-1.45,.62,1.6,1.8)
    tank(3.3,2.3,.60,1.6,3.5)
    tank(3.7,-1.0,.6,1.1,2.5)
    for x in [-.75,.30]:
        cyl('Refining stack',(x,2.65,4.4),.46,6.8,'Metal',12)
        cyl('Stack filtration crown',(x,2.65,7.9),.63,.5,'Secondary',12)
        cyl('Stack signal band',(x,2.65,7.3),.48,.14,'Glow',12)
    box('Delivery weighbridge',(0,-3.72,.73),(8.2,2.6,.3),'Metal',.05)
    hazard(0,-5.04,.91,7.8)
    for s in [-1,1]:
        box('Crane upright',(s*3.4,-3.55,2.55),(.42,.5,4.0),'Secondary',.07)
        box('Crane status strip',(s*3.4,-3.82,2.7),(.10,.035,2.6),'Glow',.01)
    box('Transfer gantry',(0,-3.55,4.77),(7.8,.85,.68),'Armor',.10)
    box('Magnetic crane trolley',(1.7,-3.55,4.26),(1.15,1.3,.4),'Accent',.07)
    beam('Crane suspension',(1.7,-3.55,4.1),(1.7,-3.55,2.25),.055,'Metal')
    cyl('Magnetic freight clamp',(1.7,-3.55,2.1),.43,.28,'Glow',8)
    beam('Refining overhead pipe',(0,1.3,4.2),(3.3,1.3,4.2),.22,'Accent')
    vents(-1.5,-1.46,1.8,1.2,1.25)

def barracks():
    base(11,10)
    body('Troop habitation block',0,1.1,2.3,8.6,5.7,3.4)
    body('Security entrance',0,-2.3,1.85,4.4,2.6,2.5)
    door(0,-3.69,.6,2.4,2.1)
    windows(0,-1.84,3.6,6.5,6,.65)
    for s in [-1,1]:
        vents(s*3.22,-1.86,1.65,1.22,1.2)
        box('Habitat side radiator',(s*4.40,1.35,2.6),(.16,3.6,1.7),'Metal',.035)
        for i in range(4): box('Habitat radiator fin',(s*4.51,.1+i*.84,2.6),(.16,.12,1.6),'Accent',.025)
    box('Training apron',(0,-4.28,.62),(7.3,1.05,.09),'Secondary',.02)
    for x in [-2.6,2.6]:
        beam('Flagpole',(x,-3.6,.65),(x,-3.6,4.6),.055,'Metal')
        box('Faction pennant',(x+.43,-3.6,4.10),(.82,.08,.84),'Secondary',.025)
        box('Pennant insignia',(x+.43,-3.65,4.1),(.12,.025,.52),'Glow',.01)
    for s in [-1,1]: box('Rooftop ventilation unit',(s*2.3,1.0,4.52),(1.9,2.1,.55),'Metal',.09)
    beacon(0,2.2,4.23,1.5)

def factory():
    base(13.7,12)
    body('Heavy assembly building',0,1.1,2.8,11.2,8.2,4.3)
    box('Assembly bay dark interior',(0,-3.12,2.3),(6.5,.18,3.4),'Metal',.05)
    for s in [-1,1]:
        box('Assembly bay retracted door',(s*2.56,-3.26,2.28),(1.38,.16,3.3),'Secondary',.06)
        for i in range(6): box('Assembly door reinforcement',(s*2.56,-3.36,1+i*.48),(1.21,.06,.11),'Accent',.01)
        box('Bay vertical guide light',(s*3.42,-3.26,2.35),(.10,.08,3.5),'Glow',.01)
        box('Apron drive guide',(s*2.35,-4.66,.65),(.12,2.2,.07),'Glow',.01)
    box('Assembly bay lintel',(0,-3.2,4.22),(7.3,.75,.56),'Secondary',.10)
    hazard(0,-3.6,4.23,6.6)
    box('Industrial roof spine',(0,1.5,5.43),(3.1,7.5,.6),'Secondary',.09)
    for i in range(5): box('Roof sawtooth vent',(0,-1+i*1.25,5.88),(2.5,.50,.55),'Metal',.07,(math.radians(12),0,0))
    for s in [-1,1]:
        body('Factory auxiliary module',s*5.53,1,1.8,1.5,5.3,2.4)
        vents(s*4.72,-3.14,2.55,1.1,1.6)
    box('Roof crane bridge',(0,2.3,6.72),(9.4,.55,.55),'Accent',.07)
    for s in [-1,1]: box('Roof crane mount',(s*4.3,2.3,5.89),(.38,.55,1.5),'Secondary',.05)
    beacon(-4.4,3.0,5.12,1.65)

def airfield():
    base(15,13)
    body('Flight operations hangar',0,3.5,2.5,11.5,5.2,3.8)
    box('Hangar dark opening',(0,.79,2.0),(7.4,.14,2.7),'Metal',.03)
    box('Hangar blast door upper',(0,.68,3.1),(7.2,.13,.7),'Secondary',.05)
    for s in [-1,1]: box('Hangar light post',(s*3.87,.65,2.1),(.12,.08,2.9),'Glow',.01)
    cyl('VTOL landing platform',(0,-2.0,.72),4.45,.32,'Secondary',32)
    cyl('Landing platform inset',(0,-2.0,.89),4.10,.08,'Metal',32)
    # Segmented landing ring remains readable in game view.
    for i in range(16):
        a=i*math.pi/8
        box('Landing perimeter light',(math.cos(a)*3.85,-2.0+math.sin(a)*3.85,.955),(.53,.12,.035),'Glow',.005,(0,0,a+math.pi/2))
    for s in [-1,1]: box('Landing H stem',(s*.8,-2.0,.959),(.23,2.4,.04),'Armor',.005)
    box('Landing H crossbar',(0,-2.0,.96),(1.83,.23,.04),'Armor',.005)
    body('Flight control tower',-5.6,3.6,4.0,2.2,2.5,6.8)
    box('Flight control glass',(-5.6,2.29,6.2),(1.7,.09,.84),'Glass',.02)
    box('Flight control glass band',(-5.6,2.22,5.72),(1.78,.045,.10),'Glow',.01)
    beacon(-5.6,3.6,7.66,1.5)
    for y in [-4.1,-1.0]:
        tank(5.85,y,.59,.58,1.55)
    for x in [-3.4,0,3.4]:
        box('Hangar rooftop skylight',(x,3.6,4.7),(1.7,3.5,.11),'Glass',.03)

def tech():
    base(10.8,10)
    body('Research laboratory',0,.8,2.45,8.3,6.8,3.8)
    windows(0,-2.69,2.75,6.8,5,1.3)
    door(-2.1,-2.68,.6,1.6,1.8)
    body('Elevated research core',1,1.35,5.60,3.9,3.8,2.1)
    for s in [-1,1]:
        box('Quantum core translucent fin',(1+s*1.91,1.35,5.62),(.11,2.6,1.2),'Glass',.01)
        box('Quantum core light rail',(1+s*1.98,1.35,5.12),(.08,2.6,.11),'Glow',.01)
    dish(1,1.35,7.65,2.1)
    tank(-3.02,1.6,4.59,.70,1.48)
    for s in [-1,1]:
        box('Laboratory entrance bollard',(s*3.5,-3.8,1.16),(.40,.45,1.15),'Secondary',.07)
        box('Laboratory bollard light',(s*3.5,-4.05,1.40),(.18,.025,.45),'Glow',.01)
    beacon(-3.2,3.2,4.65,1.4)
    for i in range(3): box('Research chassis ribs',(3.7,-.6+i*1.4,2.45),(.5,.18,3.2),'Accent',.035)

def turret():
    base(7.4,7.4)
    slab('Concrete defense bunker',(0,0,1.5),(5.9,5.9),(4.5,4.5),1.8,'Armor')
    for s in [-1,1]:
        box('Bunker firing slit',(s*1.8,-2.64,1.59),(1.0,.065,.21),'Metal',.016)
    cyl('Gun turret race',(0,0,2.60),1.85,.48,'Metal',24)
    slab('Welded cannon turret',(0,.15,3.25),(3.6,3.2),(2.7,2.5),1.15,'Secondary')
    for s in [-1,1]:
        beam('Heavy defense cannon',(s*.58,-1.13,3.42),(s*.58,-4.22,3.42),.135,'Metal',16)
        beam('Cannon thermal sleeve',(s*.58,-1.13,3.42),(s*.58,-2.55,3.42),.23,'Armor',16)
        beam('Cannon fume extractor',(s*.58,-2.07,3.42),(s*.58,-2.55,3.42),.30,'Secondary',16)
        cyl('Cannon muzzle brake',(s*.58,-4.13,3.42),.21,.32,'Metal',16,(math.pi/2,0,0))
        cyl('Cannon dark bore',(s*.58,-4.302,3.42),.126,.016,'Metal',16,(math.pi/2,0,0))
        for i in range(3):
            beam('Smoke discharger',(s*1.48,-.13-i*.27,3.54),(s*1.81,-.38-i*.27,3.82),.08,'Metal',8)
    cyl('Commander hatch',(0,.4,3.9),.55,.12,'Armor',20)
    box('Optical range finder',(0,-.55,3.99),(.59,.61,.32),'Armor',.045)
    box('Range finder lens',(0,-.87,3.99),(.4,.035,.19),'Glass',.015)
    beacon(1.1,1.1,3.90,1.1)
def air_defense():
    base(7.4,7.4)
    slab('Air defense command bunker',(0,0,1.44),(5.9,5.9),(4.7,4.7),1.7,'Armor')
    door(0,-2.9,.58,1.3,1.3)
    cyl('AA rotating base',(0,0,2.58),1.80,.55,'Metal',16)
    cyl('AA ring light',(0,0,2.88),1.69,.08,'Glow',16)
    box('Launcher central trunnion',(0,.2,3.52),(1.50,1.7,1.55),'Secondary',.12)
    # Pods point upward and forward; each mouth carries four modeled rockets.
    ang=math.radians(-32)
    for s in [-1,1]:
        x=s*1.72
        box('Four cell missile pod',(x,-.1,4.23),(1.80,2.9,1.85),'Secondary',.13,(ang,0,0))
        box('Missile pod armor flank',(x+s*.95,-.1,4.23),(.15,2.65,1.6),'Armor',.04,(ang,0,0))
        # Local front face y=-1.5 rotated around X.
        for dx in [-.42,.42]:
            for dz in [-.42,.42]:
                yy=-.1-1.52*math.cos(ang)-dz*math.sin(ang)
                zz=4.23-1.52*math.sin(ang)+dz*math.cos(ang)
                cyl('Individual missile launch tube',(x+dx,yy,zz),.34,.24,'Metal',12,(math.pi/2+ang,0,0))
                cyl('Missile warhead cap',(x+dx,yy-.14*math.cos(ang),zz-.14*math.sin(ang)),.23,.10,'Glow',12,(math.pi/2+ang,0,0))
        box('Launcher top warning stripe',(x,-.04,5.28),(1.1,1.75,.10),'Accent',.02,(ang,0,0))
    beam('Search radar mast',(0,1.45,3.0),(0,1.45,6.95),.16,'Metal')
    box('Phased array radar',(0,1.3,6.92),(2.2,.45,1.3),'Armor',.13,(math.radians(-8),0,0))
    for x in [-.7,0,.7]:
        for z in [6.6,6.95,7.3]: box('Radar phased array emitter',(x,1.035,z),(.40,.035,.15),'Glow',.01)

def superweapon():
    base(13,12)
    slab('Strategic missile command bunker',(0,0,1.51),(10.7,10),(9.0,8.4),1.9,'Armor')
    for s in [-1,1]:
        body('Launch control wing',s*4.23,.4,2.52,2.7,5.6,3.3)
        vents(s*4.23,-2.5,2.60,1.72,1.5)
        tank(s*4.05,3.1,.58,.80,2.9)
    door(0,-4.98,.61,2.8,2.3)
    cyl('Armored silo ring',(0,.35,2.7),2.65,.50,'Metal',32)
    cyl('Silo launch well',(0,.35,2.99),2.29,.05,'Metal',32)
    for s in [-1,1]:
        box('Retracted reinforced silo door',(s*2.29,.35,3.11),(1.21,4.65,.30),'Secondary',.065)
        for i in range(6):box('Silo blast door reinforcement',(s*2.29,-1.47+i*.73,3.31),(1.1,.15,.17),'Armor',.025)
    cyl('Strategic missile booster',(0,.35,5.38),.67,4.85,'Armor',24)
    cyl('Missile tapered nose',(0,.35,8.84),.67,2.12,'Secondary',24,radius_top=.045)
    for z in [3.31,4.02,6.40,7.72]:cyl('Missile assembly joint',(0,.35,z),.695,.10,'Metal',24)
    cyl('Missile identification band',(0,.35,7.10),.685,.38,'Glow',24)
    for i in range(4):
        a=i*math.pi/2
        box('Missile stabilizing fin',(math.cos(a)*.76,.35+math.sin(a)*.76,3.75),(.11,1.2,1.55),'Secondary',.03,(0,0,a))
    for z in [3.35,4.4,5.45,6.5,7.55]:
        box('Launch service gantry crossbar',(1.44,1.35,z),(.98,.18,.16),'Metal',.025)
    for x in [.95,1.93]:beam('Launch service gantry upright',(x,1.35,3.0),(x,1.35,8.0),.105,'Metal',10)
    hazard(0,-5.02,1.4,3.9)
    dish(-4.18,.3,4.96,.80)
    beacon(4.18,.3,4.54,1.6)
BUILDERS=[command,power,refinery,barracks,factory,airfield,tech,turret,air_defense,superweapon]

def sandbag(x,y,z,turn=0):
    box('Hessian defensive sandbag',(x,y,z),(.76,.41,.29),'Armor',.095,(0,0,turn))
    box('Sandbag stitched seam',(x,y-.207,z),(.52,.012,.021),'Secondary',.002,(0,0,turn))

def crate(x,y,z,scale=.7):
    box('Supply crate',(x,y,z+scale/2),(scale,scale,scale),'Secondary',.03)
    for sx in [-1,1]:
        for yy in [-.31,.31]:
            box('Crate reinforcing strap',(x+sx*scale*.49,y+yy*scale,z+scale/2),(.029,scale*.11,scale*.97),'Metal',.008)
    for yy in [-.31,.31]:box('Crate lid band',(x,y+yy*scale,z+scale+.016),(scale,.08,.025),'Metal',.004)

def field_service_details(ident):
    # Built geometry, rather than flat decoration, makes bases retain fidelity when zoomed in.
    sizes={'Command':(11.5,10),'Power':(10,9),'Refinery':(13,11),'Barracks':(11,10),'Factory':(13.7,12),'Airfield':(15,13),'Tech':(10.8,10),'Turret':(7.4,7.4),'AirDefense':(7.4,7.4),'Superweapon':(13,12)}
    w,d=sizes[ident]
    for x in [-w*.33,w*.33]:
        for row in range(2):
            for i in range(3):sandbag(x+(i-1)*.71+(row%2)*.13,-d*.465,.77+row*.26,.03*(i-1))
    if ident not in ['Turret','AirDefense']:
        # Readable outdoor stores and utility drums on the foundation edge.
        for i in range(3):crate(w*.405,-d*.23+i*.72,.59,.59)
        crate(w*.405,-d*.23+.36,1.18,.55)
        for i in range(2):
            xx=-w*.408;yy=d*.27-i*.72
            cyl('Fuel drum',(xx,yy,1.06),.31,.90,'Secondary',20)
            for z in [.77,1.35]:cyl('Fuel drum reinforcing band',(xx,yy,z),.324,.054,'Metal',20)
            cyl('Fuel drum filler cap',(xx+.12,yy,1.526),.046,.024,'Metal',10)
        for sx in [-1,1]:
            beam('Exterior conduit',(sx*w*.445,-d*.15,.85),(sx*w*.445,d*.28,.85),.043,'Metal',8)
            for yy in [-d*.15,d*.10,d*.28]:box('Service conduit clamp',(sx*w*.445,yy,.85),(.15,.048,.15),'Secondary',.012)
    if ident in ['Command','Barracks','Tech','Factory','Refinery']:
        top={'Command':3.17,'Barracks':4.20,'Tech':4.60,'Factory':5.10,'Refinery':3.99}[ident]
        xx=-2.4 if ident=='Refinery' else 2.1
        yy=1.1 if ident!='Refinery' else 1.5
        box('Rooftop air conditioning unit',(xx,yy,top+.33),(1.3,1.25,.66),'Metal',.045)
        cyl('HVAC fan grille',(xx,yy,top+.68),.49,.041,'Secondary',24)
        for angle in [0,math.pi/2]:
            box('HVAC visible fan blade',(xx,yy,top+.715),(.75,.13,.024),'Metal',.007,(0,0,angle))
        for i in range(7):box('HVAC radiator grille',(xx-.47+i*.155,yy-.639,top+.3),(.048,.023,.4),'Armor',.004)
        for x in [xx-.49,xx+.49]:beam('HVAC connection pipe',(x,yy+.6,top+.10),(x,yy+1.03,top+.10),.065,'Metal',10)
    if ident in ['Factory','Airfield','Refinery']:
        for sx in [-1,1]:
            for y in [-d*.38,-d*.26]:
                box('Painted apron lane marking',(sx*1.91,y,.588),(.13,.82,.015),'Accent',.001)
    labels={'Command':'HQ 01','Power':'POWER','Refinery':'SUPPLY','Barracks':'INFANTRY','Factory':'WAR FACTORY','Airfield':'AIR BASE','Tech':'RESEARCH','Turret':'BUNKER','AirDefense':'SAM','Superweapon':'STRATEGIC'}
    # Embossed service sign, legible under oblique lighting without a texture dependency.
    sign_width=min(w*.44,5.0)
    box('Facility identification sign',(0,-d*.493,1.09),(sign_width,.035,.51),'Metal',.014)
    bpy.ops.object.text_add(location=(0,-d*.498-.025,1.04))
    sign=bpy.context.object;sign.rotation_euler=(math.pi/2,0,0)
    sign.data.body=labels[ident];sign.data.align_x='CENTER';sign.data.align_y='CENTER';sign.data.size=.34 if len(labels[ident])<9 else .28;sign.data.extrude=.002
    bpy.ops.object.convert(target='MESH');finish(bpy.context.object,'Embossed facility stencil','Accent')

def join_asset(name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts: o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]
    bpy.ops.object.join()
    o=bpy.context.object; o.name=name; o.data.name=name+'_Mesh'
    bpy.context.scene.cursor.location=(0,0,0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    # FBX readers triangulate concave bevel caps differently. Resolve every face
    # in Blender so Unity never has to infer a diagonal or discard an ngon.
    triangulate=o.modifiers.new('Explicit game engine triangles','TRIANGULATE')
    triangulate.quad_method='BEAUTY'
    triangulate.ngon_method='BEAUTY'
    bpy.ops.object.modifier_apply(modifier=triangulate.name)
    # A bevel on very thin cylinders can collapse an original side face to a
    # zero-area seam. It contributes no visible surface; exclude it from FBX.
    bm=bmesh.new(); bm.from_mesh(o.data)
    zero_area=[f for f in bm.faces if f.calc_area()<1e-10]
    if zero_area: bmesh.ops.delete(bm,geom=zero_area,context='FACES_ONLY')
    bm.to_mesh(o.data); bm.free()
    o.data.validate(verbose=False)
    o.data.update()
    o['faction']=fac; o['art_role']=name.split('_',1)[1]
    o['style']='User reference: layered low-rise graphite megastructure architecture, cyan window arrays, cantilever roofs, dense exposed services'
    o['units']='meters'; o['ground_origin']=True
    return o

def preview_scene(objects,faction):
    # Orthographic catalogue render with all ten structures.
    bpy.ops.object.camera_add(location=(37,-59,62))
    camera=bpy.context.object; camera.name='Catalogue_Camera'
    target=Vector((0,0,1.5)); camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='ORTHO'; camera.data.ortho_scale=128
    scene=bpy.context.scene; scene.camera=camera
    scene.render.engine='BLENDER_WORKBENCH'
    scene.display.shading.light='STUDIO'
    scene.display.shading.studio_light='paint.sl'
    scene.display.shading.color_type='MATERIAL'
    scene.display.shading.show_shadows=True
    scene.display.shading.show_cavity=True
    scene.display.shading.cavity_type='BOTH'
    scene.display.shading.curvature_ridge_factor=1.2
    scene.display.shading.curvature_valley_factor=.8
    scene.display.shading.show_specular_highlight=True
    scene.display.shading.background_type='WORLD'
    scene.world.color=(.027,.034,.046)
    scene.render.resolution_x=1800; scene.render.resolution_y=1150; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.filepath=os.path.join(SRC,faction+'_Structures_Preview.png')
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SRC,faction+'_Structures.blend'))
    bpy.ops.render.render(write_still=True)

def main():
    global parts,mat,fac
    sys.path.insert(0,os.path.dirname(__file__))
    import ref_structures
    ref_structures.install(globals())
    report=[]
    for faction in FACTIONS:
        fac=faction
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.context.scene.unit_settings.system='METRIC'; bpy.context.scene.unit_settings.scale_length=1
        world=bpy.data.worlds.new('Industrial studio'); bpy.context.scene.world=world
        mat=palette(faction)
        finished=[]
        for ident,build in zip(IDS,BUILDERS):
            parts=[]; build(); field_service_details(ident)
            obj=join_asset(faction+'_'+ident)
            tris=sum(len(p.vertices)-2 for p in obj.data.polygons)
            dims=[round(v,3) for v in obj.dimensions]
            if tris>=40000: raise RuntimeError(obj.name+' exceeds triangle budget: '+str(tris))
            nontriangles=sum(len(p.vertices)!=3 for p in obj.data.polygons)
            degenerate=sum(p.area<1e-10 for p in obj.data.polygons)
            if nontriangles or degenerate: raise RuntimeError(obj.name+' invalid mesh: nontriangles='+str(nontriangles)+', degenerate='+str(degenerate))
            anchors=ref_structures.add_anchors(obj,ident)
            bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,obj.name+'.fbx'),use_selection=True,object_types={'MESH','EMPTY'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_NONE',axis_forward='-Z',axis_up='Y',global_scale=1.0,bake_anim=False,use_mesh_modifiers=True,mesh_smooth_type='FACE',use_tspace=False,path_mode='AUTO',add_leaf_bones=False)
            report.append({'name':obj.name,'vertices':len(obj.data.vertices),'triangles':tris,'dimensions_m':dims,'material_slots':len(obj.data.materials),'nontriangular_faces':nontriangles,'degenerate_faces':degenerate,'anchors_blender_m':{a.name:[round(v,3) for v in a.location] for a in anchors},'fbx_bytes':os.path.getsize(os.path.join(OUT,obj.name+'.fbx'))})
            finished.append(obj)
            idx=len(finished)-1
            obj.location=((idx%5-2)*22,(idx//5-.5)*37,0)
            print('BUILT',obj.name,tris,dims,flush=True)
        preview_scene(finished,faction)
    with open(os.path.join(SRC,'structure_validation.json'),'w',encoding='utf-8') as f: json.dump(report,f,indent=2)
    print('COMPLETE: exported',len(report),'original building meshes; maximum triangles',max(r['triangles'] for r in report),flush=True)

if __name__=='__main__': main()
