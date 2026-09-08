"""Build the original Vanguard and Dynasty army miniatures; run with Blender --background --python.

All measurements are meters, +Y is nose/forward and +Z is up. FBX exports are single
mesh, multi-material assets at a ground-centered origin. No external textures needed.
"""
import bpy
import bmesh
import math
import json
import os
import sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ref_tanks import build_tank as reference_tank
from ref_aircraft import build_aircraft as reference_aircraft

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '../..'))
OUT = os.path.join(ROOT, 'Assets/Armies/Models/Units')
SOURCE = os.path.join(ROOT, 'ArtSource/Units')
os.makedirs(OUT, exist_ok=True)
os.makedirs(SOURCE, exist_ok=True)
IDS = ['Worker','Rifle','Rocket','Engineer','Commando','Scout','APC','Tank','Heavy','Artillery','AntiAir','Support','Drone','Helicopter','Fighter','Bomber']
F = None
M = {}
PARTS = []
REPORT = []
arguments = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
def argument(name, default=''):
    return arguments[arguments.index(name) + 1] if name in arguments else default
FILTER = [key for key in argument('--filter').split(',') if key]
FACTIONS = [argument('--faction')] if argument('--faction') else ['Vanguard', 'Dynasty']
assert not FILTER or all(key in IDS for key in FILTER), 'Unknown filtered unit id'
assert all(f in ['Vanguard', 'Dynasty'] for f in FACTIONS), 'Unknown faction'

def material(name, color, metal=.18, rough=.72, emission=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    bs = m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = (*color,1)
    bs.inputs['Metallic'].default_value = metal
    bs.inputs['Roughness'].default_value = rough
    if emission:
        bs.inputs['Emission Color'].default_value = (*color,1)
        bs.inputs['Emission Strength'].default_value = emission
    return m

def use(obj, mat='Armor', bevel=0):
    obj.data.materials.append(M[mat])
    if bevel:
        mod=obj.modifiers.new('Machined edge chamfer', 'BEVEL')
        mod.width=bevel
        mod.segments=2 if bevel >= .05 else 1
        mod.affect='EDGES'
        bpy.context.view_layer.objects.active=obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
    PARTS.append(obj)
    return obj

def box(name, loc, size, mat='Armor', bevel=.055, rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o=bpy.context.object
    o.name=name
    o.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if rot: o.rotation_euler=rot
    return use(o,mat,min(bevel,min(size)*.24))

def cylinder(name, loc, radius, depth, mat='Metal', axis='Z', verts=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc)
    o=bpy.context.object
    o.name=name
    if axis=='X': o.rotation_euler[1]=math.pi/2
    if axis=='Y': o.rotation_euler[0]=math.pi/2
    return use(o,mat,.025 if radius>.15 else 0)

def rod(name,a,b,r,mat='Metal',verts=10):
    d=Vector(b)-Vector(a)
    o=cylinder(name,(Vector(a)+Vector(b))/2,r,d.length,mat,verts=verts)
    o.rotation_euler=d.to_track_quat('Z','Y').to_euler()
    return o

def hull(name,loc,size,mat='Armor',taper=.75,shift=0):
    x,y,z=size
    vx=[(-x/2,-y/2,-z/2),(x/2,-y/2,-z/2),(x/2,y/2,-z/2),(-x/2,y/2,-z/2),
        (-x*taper/2,-y*.41+shift,z/2),(x*taper/2,-y*.41+shift,z/2),(x*taper/2,y*.4+shift,z/2),(-x*taper/2,y*.4+shift,z/2)]
    me=bpy.data.meshes.new(name)
    me.from_pydata(vx,[],[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])
    me.update()
    o=bpy.data.objects.new(name,me)
    bpy.context.collection.objects.link(o)
    o.location=loc
    return use(o,mat,.045)

def prism(name,points,z,depth,mat='Armor'):
    n=len(points)
    verts=[(x,y,z+zz) for zz in (-depth/2,depth/2) for x,y in points]
    faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
    faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    me=bpy.data.meshes.new(name)
    me.from_pydata(verts,[],faces)
    me.update()
    o=bpy.data.objects.new(name,me)
    bpy.context.collection.objects.link(o)
    return use(o,mat,.035)

def glowline(loc,size): return box('Painted tactical identification stripe',loc,size,'Glow',.008)

def wheel(x,y,z=.55,r=.48):
    cylinder('All-terrain tire',(x,y,z),r,.38,'Rubber','X',24)
    s=1 if x>0 else -1
    cylinder('Stamped wheel rim',(x+s*.205,y,z),r*.63,.055,'Secondary','X',20)
    cylinder('Axle cap',(x+s*.245,y,z),r*.24,.08,'Metal','X',12)
    for a in range(0,360,60):
        angle=math.radians(a)
        cylinder('Wheel lug bolt',(x+s*.253,y+math.sin(angle)*r*.42,z+math.cos(angle)*r*.42),r*.046,.025,'Metal','X',6)
    for i in range(22):
        a=i*math.tau/22
        box('Tire traction block',(x,y+math.sin(a)*(r-.009),z+math.cos(a)*(r-.009)),(.40,.13,r*.11),'Rubber',.012,rot=(-a,0,.13 if i%2 else -.13))

def tracks(w=2.7,l=4.3,heavy=False):
    # Open, capsule-shaped track loops keep the suspension readable from game camera height.
    for s in [-1,1]:
        x=s*w/2
        box('Suspension backing',(x-s*.05,0,.5),(.41,l-.55,.57),'Metal',.06)
        wheel_count=7 if heavy else 6
        for i in range(wheel_count):
            y=-l*.34+i*l*.68/(wheel_count-1)
            cylinder('Rubber road wheel',(x+s*.16,y,.49),.32,.32,'Rubber','X',20)
            cylinder('Cast suspension wheel',(x+s*.335,y,.49),.257,.045,'Armor','X',16)
            cylinder('Wheel bearing',(x+s*.368,y,.49),.096,.055,'Metal','X',10)
            rod('Trailing suspension arm',(x-s*.2,y-.21,.70),(x-s*.2,y,.43),.08,'Metal',8)
        for end in [-1,1]:
            cylinder('Track drive sprocket',(x,end*(l/2-.44),.52),.35,.54,'Metal','X',20)
            cylinder('Drive hub',(x+s*.30,end*(l/2-.44),.52),.20,.075,'Secondary','X',12)
        run=l-.88
        count=int(run/.18)+1
        for i in range(count):
            y=-run/2+i*run/(count-1)
            for z in [.10,.94]:
                box('Individual steel track shoe',(x,y,z),(.76,.17,.105),'Metal',.012)
                box('Rubber track pad',(x,y,z+(-.052 if z<.2 else .052)),(.49,.115,.035),'Rubber',.006)
        for end in [-1,1]:
            for i in range(9):
                a=-math.pi/2+i*math.pi/8
                y=end*(run/2+math.cos(a)*.42)
                z=.52+math.sin(a)*.42
                box('Curved return track shoe',(x,y,z),(.76,.16,.105),'Metal',.009,rot=(end*(math.pi/2-a),0,0))
        box('Armored track fender',(x,0,1.10),(.92,l*1.02,.16),'Armor',.05)
        for i in range(5):
            y=-l*.33+i*l*.66/4
            box('Bolted segmented side skirt',(x+s*.43,y,.97),(.10,l*.15,.32),'Armor',.02)
            for yy in [y-l*.045,y+l*.045]:
                cylinder('Skirt attachment bolt',(x+s*.495,yy,1.06),.029,.026,'Metal','X',6)
        for y in [-l*.39,l*.39]:
            box('Flexible mud flap',(x,y,.61),(.77,.06,.68),'Rubber',.018)

def lights(w,y,z):
    for s in [-1,1]:
        box('Headlight housing',(s*w,y,z),(.3,.1,.18),'Metal',.025)
        glowline((s*w,y+.06,z),(.22,.024,.065))

def vents(x,y,z,n=5,along='Y'):
    for i in range(n):
        box('Radiator louver',(x,y+(i-(n-1)/2)*.14,z),(.48,.055,.06),'Metal',.005)

def antenna(x,y,z,h=1):
    cylinder('Comms mount',(x,y,z),.12,.16,'Secondary')
    rod('Whip aerial',(x,y,z),(x,y,z+h),.022,'Metal',6)
    cylinder('Aerial emitter',(x,y,z+h),.044,.1,'Glow',verts=8)

def gun(x,y,z,length=1.8,r=.095,thick=False):
    rod('Barrel',(x,y,z),(x,y+length,z),r,'Metal')
    rod('Barrel armored jacket',(x,y,z),(x,y+length*.5,z),r*1.6,'Secondary')
    box('Muzzle brake',(x,y+length,z),(.25 if not thick else .38,.31,.22 if not thick else .32),'Secondary',.05)
    box('Muzzle bore',(x,y+length+.16,z),(.12 if not thick else .22,.008,.1 if not thick else .18),'Rubber',0)

def turret(x,y,z,scale=1,dual=False):
    start=len(PARTS)
    cylinder('Turret race',(x,y,z-.25),.6*scale,.2,'Metal',verts=16)
    hull('Welded armored turret',(x,y,z), (1.9*scale,2.02*scale,.69*scale),taper=.69)
    hull('Gun mantlet',(x,y+.79*scale,z),(.66*scale,.50*scale,.47*scale),'Secondary',.85)
    box('Turret optic',(x+.4*scale,y+.7*scale,z+.18*scale),(.24,.16,.22),'Glass',.025)
    if dual:
        for s in [-1,1]: gun(x+s*.3*scale,y+.65*scale,z,2.75*scale,.095*scale)
    else: gun(x,y+.6*scale,z,2.8*scale,.105*scale)
    cylinder('Commander hatch',(x-.42*scale,y-.24*scale,z+.39*scale),.32*scale,.11*scale,'Secondary',verts=20)
    cylinder('Loader hatch',(x+.43*scale,y-.32*scale,z+.39*scale),.26*scale,.09*scale,'Armor',verts=16)
    for sx in [-1,1]:
        for i in range(3):
            rod('Smoke grenade launcher',(x+sx*.90*scale,y+(.19-i*.19)*scale,z+.10*scale),(x+sx*1.1*scale,y+(.40-i*.19)*scale,z+.33*scale),.063*scale,'Metal',8)
        box('Turret side equipment bin',(x+sx*.97*scale,y-.33*scale,z+.10*scale),(.16*scale,.9*scale,.32*scale),'Secondary',.025)
        for yy in [-.61,-.10]:
            rod('Equipment bin grab handle',(x+sx*1.065*scale,y+yy*scale,z+.1*scale),(x+sx*1.065*scale,y+(yy+.17)*scale,z+.1*scale),.021*scale,'Metal',6)
    box('Turret bustle basket',(x,y-.98*scale,z+.13*scale),(1.62*scale,.50*scale,.37*scale),'Metal',.03)
    for xx in [-.53,0,.53]:
        cylinder('Canvas equipment roll',(x+xx*scale,y-1.08*scale,z+.40*scale),.14*scale,.43*scale,'Secondary','X',12)
    rod('Roof machine gun pintle',(x-.39*scale,y-.12*scale,z+.47*scale),(x-.39*scale,y-.12*scale,z+.68*scale),.044*scale,'Metal',8)
    box('Roof machine gun receiver',(x-.39*scale,y+.02*scale,z+.7*scale),(.12*scale,.33*scale,.12*scale),'Metal',.012)
    rod('Roof machine gun barrel',(x-.39*scale,y+.13*scale,z+.7*scale),(x-.39*scale,y+.69*scale,z+.7*scale),.028*scale,'Metal',8)
    for o in PARTS[start:]:o['articulation']='TurretPivot';o['pivot']=(x,y,z-.25)

def humanoid(kind):
    # Human proportions, fatigues, body armor, webbing, helmet and visible skin.
    # Staggered legs and supported weapon pose read clearly from an RTS camera.
    for s in [-1,1]:
        x=s*.16; y=s*.07
        box('Combat boot',(x,y+.08,.13),(.24,.39,.25),'Rubber',.06)
        rod('Fatigue trouser calf',(x,y,.31),(x,y-.03,.70),.115,'Armor',12)
        box('Reinforced kneepad',(x,y+.105,.72),(.21,.10,.19),'Secondary',.05)
        rod('Fatigue trouser thigh',(x,y-.03,.79),(x,y-.04,1.08),.135,'Armor',12)
        box('Cargo pocket',(x+s*.12,y-.01,.94),(.12,.22,.22),'Secondary',.025)
    hull('Fatigue pelvis',(0,-.03,1.07),(.55,.35,.28),'Armor',.90)
    hull('Uniform torso',(0,-.015,1.41),(.60,.38,.54),'Armor',.88)
    hull('Ballistic vest',(0,.07,1.43),(.57,.44,.44),'Secondary',.92)
    for x in [-.18,0,.18]:
        box('Magazine pouch',(x,.31,1.40),(.14,.14,.20),'Armor',.025)
        box('Webbing strap',(x,.30,1.59),(.045,.055,.16),'Metal',.008)
    box('Webbing belt',(0,0,1.15),(.58,.41,.09),'Metal',.025)
    cylinder('Neck',(0,0,1.77),.085,.16,'Skin',verts=12)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,radius=1,location=(0,.018,1.90))
    head=bpy.context.object;head.scale=(.155,.16,.20);use(head,'Skin')
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,radius=1,location=(0,-.005,2.008))
    helmet=bpy.context.object;helmet.scale=(.205,.22,.145);use(helmet,'Armor')
    box('Helmet brim',(0,.117,1.997),(.40,.25,.053),'Secondary',.025)
    box('Protective goggles',(0,.166,1.939),(.272,.053,.079),'Glass',.022)
    for s in [-1,1]:
        rod('Chin strap',(s*.151,.015,1.99),(s*.096,.112,1.77),.012,'Secondary',6)
        shoulder=(s*.32,0,1.62);elbow=(s*.38,.06,1.32);hand=(s*.20,.40,1.36)
        rod('Uniform sleeve',shoulder,elbow,.106,'Armor',12)
        rod('Forearm sleeve',elbow,hand,.09,'Armor',12)
        box('Fingerless tactical glove',hand,(.155,.17,.12),'Secondary',.035)
        box('Shoulder insignia',(s*.423,.005,1.59),(.02,.11,.09),'Glow',.009)
    box('Field rucksack',(0,-.29,1.44),(.43,.24,.48),'Secondary',.065)
    for x in [-.17,.17]:box('Pack buckle',(x,-.427,1.46),(.053,.021,.06),'Metal',.007)
    cylinder('Rolled ground mat',(0,-.29,1.78),.107,.49,'Armor','X',12)
    if kind=='Rocket':
        rod('Shoulder rocket tube',(.36,-.32,1.72),(.36,1.0,1.72),.133,'Secondary',16)
        cylinder('Launcher muzzle',(.36,1.02,1.72),.15,.08,'Metal','Y',16)
        cylinder('Launcher bore',(.36,1.069,1.72),.115,.009,'Rubber','Y',16)
        box('Launcher optical sight',(.43,.31,1.89),(.13,.19,.105),'Glass',.02)
        for x in [-.14,.10]:
            rod('Spare rocket tube',(x,-.46,1.15),(x,-.46,1.97),.073,'Secondary',12)
            cylinder('Rocket end cap',(x,-.46,1.98),.079,.06,'Armor',verts=12)
    elif kind=='Engineer':
        box('Field tool case',(-.23,.37,1.12),(.25,.29,.26),'Secondary',.035)
        box('Welding unit',(.19,.57,1.35),(.15,.28,.15),'Metal',.025)
        rod('Welding nozzle',(.19,.71,1.36),(.19,.99,1.38),.034,'Metal',8)
        box('Engineer helmet stripe',(0,.06,2.143),(.087,.22,.018),'Glow',.005)
        cylinder('Repair gas cylinder',(.17,-.49,1.48),.096,.44,'Metal',verts=12)
    else:
        sniper=kind=='Commando'
        box('Rifle receiver',(.16,.59,1.37),(.115,.42,.13),'Metal',.018)
        box('Rifle stock',(.16,.285,1.38),(.115,.24,.14),'Secondary',.026)
        box('Curved magazine',(.16,.53,1.23),(.075,.13,.22),'Metal',.018,rot=(.14,0,0))
        rod('Rifle handguard',(.16,.80,1.39),(.16,1.08 if sniper else .99,1.39),.055,'Secondary',10)
        rod('Rifle barrel',(.16,.92,1.39),(.16,1.63 if sniper else 1.29,1.39),.027,'Metal',10)
        cylinder('Muzzle suppressor',(.16,1.63 if sniper else 1.30,1.39),.041,.14,'Metal','Y',12)
        rod('Optical sight',(.16,.58,1.49),(.16,.83,1.49),.036,'Glass',10)
        if sniper:
            for s in [-1,1]:rod('Folded precision bipod',(.16+s*.027,1.08,1.36),(.16+s*.072,1.34,1.30),.014,'Metal',6)
def worker():
    tracks(2.35,3.75)
    hull('Bulldozer engine chassis',(0,0,1.05),(2.15,3.2,.65),'Secondary',.9)
    box('Diesel engine hood',(0,.76,1.53),(1.8,1.63,.67),'Armor',.10)
    box('Radiator grille',(0,1.598,1.52),(1.49,.065,.42),'Metal',.018)
    for i in range(10):box('Radiator vertical grille',(-.64+i*.143,1.641,1.52),(.043,.035,.39),'Secondary',.006)
    box('Cab floor',(0,-.63,1.53),(1.89,1.51,.18),'Metal',.035)
    box('Operator cabin glazing',(0,-.63,2.08),(1.52,1.28,.98),'Glass',.055)
    for x in [-.79,.79]:
        for y in [-1.31,.045]:box('Rollover protection pillar',(x,y,2.09),(.13,.13,1.24),'Armor',.02)
    box('Rollover cabin roof',(0,-.63,2.76),(1.92,1.74,.16),'Armor',.045)
    for x in [-.81,.81]:
        box('Cab door frame',(x,-.63,1.82),(.045,1.31,.08),'Metal',.009)
        box('Cab access step',(x*1.24,-.93,1.12),(.43,.64,.10),'Metal',.02)
        rod('Dozer lift arm',(x,.10,.93),(x,2.27,.54),.13,'Secondary',12)
        rod('Hydraulic cylinder',(x,.40,1.36),(x,1.41,.97),.12,'Armor',12)
        rod('Chrome hydraulic piston',(x,1.31,1.02),(x,2.14,.69),.061,'Metal',12)
    box('Heavy earthmoving blade',(0,2.34,.57),(3.23,.32,.97),'Armor',.06,rot=(-.16,0,0))
    box('Replaceable blade cutting edge',(0,2.42,.13),(3.32,.16,.17),'Metal',.015)
    for x in [-1.56,1.56]:box('Blade end cheek',(x,2.25,.59),(.16,.61,.99),'Secondary',.02)
    for x in [-1.2,-.6,0,.6,1.2]:
        cylinder('Cutting edge bolt',(x,2.516,.19),.043,.03,'Secondary','Y',8)
    cylinder('Exhaust stack',(.59,.33,2.23),.092,1.25,'Metal',verts=14)
    cylinder('Exhaust rain cap',(.59,.33,2.887),.145,.055,'Metal',verts=14)
    cylinder('Warning beacon',(-.63,-.88,2.91),.10,.19,'Glow',verts=12)
    lights(.66,1.49,1.98)
    antenna(.70,-1.21,2.78,.6)
def scout():
    for s in [-1,1]:
        for y in [-1.1,1.1]:wheel(s*1.15,y,.55,.53)
    hull('Scout lower hull',(0,0,.78),(2.15,3.35,.59),'Secondary',.7)
    hull('Scout wedge',(0,.1,1.11),(2.15,3.15,.62),'Armor',.66)
    hull('Recon canopy',(0,-.12,1.53),(1.25,1.51,.48),'Glass',.65)
    box('Canopy central brace',(0,-.04,1.82),(.11,1.1,.07),'Armor',.03)
    turret(0,-.84,1.65,.44, F=='Dynasty')
    antenna(.72,-1.1,1.45,.98)
    for s in [-1,1]:glowline((s*.82,.58,1.43),(.065,.68,.035))
    lights(.76,1.55,1.0)

def apc(support=False):
    for s in [-1,1]:
        for y in [-1.65,0,1.65]:wheel(s*1.46,y,.62,.60)
    hull('Armored transport chassis',(0,0,.92),(2.7,4.9,.65),'Secondary',.88)
    hull('Troop compartment',(0,-.23,1.64),(2.68,3.94,1.26),'Armor',.78)
    hull('Sloped driving canopy',(0,1.69,1.52),(2.24,1.25,.80),'Secondary',.76)
    box('Transport front window',(0,2.07,1.83),(1.59,.12,.35),'Glass',.045,rot=(math.radians(-22),0,0))
    for s in [-1,1]:
        for y in [-1.17,-.41,.35]:
            box('Side compartment hatch',(s*1.22,y,1.67),(.10,.65,.69),'Secondary',.04)
            glowline((s*1.28,y,1.94),(.018,.41,.045))
    box('Rear disembarkation ramp',(0,-2.25,1.22),(1.49,.12,1.38),'Metal',.04)
    lights(.94,2.31,1.15)
    if support:
        cylinder('Nanoforge service core',(0,-.54,2.36),.61,.50,'Secondary',verts=12)
        cylinder('Charged core',(0,-.54,2.65),.43,.18,'Glow',verts=12)
        for s in [-1,1]:
            rod('Service arm',(s*.68,-.48,2.23),(s*1.15,-.23,2.93),.10,'Metal')
            rod('Service arm',(s*1.15,-.23,2.93),(s*.83,.82,2.75),.12,'Armor')
            cylinder('Repair emitter',(s*.83,.9,2.75),.12,.21,'Glow','Y',10)
        box('Drone docking deck',(0,-1.3,2.27),(1.66,1.10,.16),'Secondary',.05)
        glowline((0,-1.3,2.36),(.76,.08,.028))
        antenna(-.72,-1.53,2.40,.89)
    else:
        turret(0,.36,2.46,.65,F=='Dynasty')
        vents(.6,-1.4,2.31)
        antenna(-.77,-1.37,2.37,.75)

def tank(kind):
    d=F=='Dynasty'
    heavy=kind=='Heavy'
    w=3.35 if heavy else 2.67
    l=5.15 if heavy else 4.4
    tracks(w,l,heavy)
    hull('Armored lower glacis',(0,0,.87),(w+.18,l*.93,.71),'Secondary',.78)
    hull('Sloped upper hull',(0,.13,1.23),(w+.20,l*.91,.69),'Armor',.72)
    for x in [-.57,.57]: vents(x,-l*.31,1.57)
    lights(w*.38,l*.46,1.13)
    if kind in ['Tank','Heavy']:
        t=1.26 if heavy else 1
        turret(0,.05,1.96 if heavy else 1.8,t,heavy or d)
        if heavy:
            for s in [-1,1]:
                hull('Composite turret cheek armor',(s*1.07,-.13,2.01),(.48,1.89,.65),'Armor',.82)
                for y in [-.65,-.30,.05,.4]:box('Reactive armor block',(s*1.316,y,2.12),(.10,.27,.21),'Secondary',.016)
                box('Heavy shoulder armor',(s*1.56,.7,1.49),(.72,1.73,.25),'Armor',.06)
            if d:
                for s in [-1,1]:
                    cylinder('Auxiliary smoke pod',(s*.98,.86,2.29),.19,.65,'Secondary','Y',8)
        antenna(-.61,-.81,2.20 if heavy else 2.08,.69)
    elif kind=='Artillery':
        cylinder('Artillery rotation ring',(0,-.27,1.6),.87,.3,'Metal',verts=16)
        hull('Artillery mantlet',(0,-.39,1.97),(1.82,1.86,.80),'Secondary',.74)
        if d:
            # The Dynasty artillery is a six-cell heavy rocket battery.
            for x in [-.64,0,.64]:
                for z in [2.05,2.69]:
                    box('Armored launch cell',(x,.15,z),(.57,2.25,.55),'Armor',.05,rot=(math.radians(12),0,0))
                    cylinder('Rocket tube',(x,1.27,z+.24),.19,.055,'Rubber','Y',8)
                    cylinder('Rocket nose',(x,1.307,z+.24),.115,.035,'Glow','Y',8)
            antenna(-1,-1.13,1.72,.93)
        else:
            a=Vector((0,.02,2.03)); b=Vector((0,3.45,3.1))
            rod('155 millimeter howitzer barrel',a,b,.145,'Metal',20)
            rod('Howitzer thermal sleeve',a,a+(b-a)*.62,.23,'Armor',20)
            rod('Fume extractor',a+(b-a)*.48,a+(b-a)*.66,.28,'Secondary',20)
            rod('Double baffle muzzle brake',b-(b-a)*.035,b+(b-a)*.04,.23,'Metal',12)
            rod('Dark howitzer bore',b+(b-a)*.041,b+(b-a)*.043,.139,'Rubber',16)
            cylinder('Elevation hinge',(0,-.10,2.00),.43,1.25,'Metal','X')
            hull('Armored howitzer breech',(0,-.67,2.04),(1.3,1.72,.62),'Armor',.72)
        for s in [-1,1]:box('Retractable recoil stabilizer',(s*1.5,-2.22,.35),(.4,.76,.30),'Secondary',.055)
    else:
        cylinder('Radar turret base',(0,-.2,1.75),.69,.46,'Secondary',verts=12)
        for s in [-1,1]:
            box('Anti-air missile pod',(s*.98,.12,2.14),(.66,1.65,.91),'Armor',.08)
            for x in [-.17,.17]:
                for z in [-.22,.22]:
                    cylinder('SAM launch aperture',(s*.98+x,.966,2.14+z),.11,.045,'Rubber','Y',8)
                    cylinder('SAM armed indicator',(s*.98+x,.994,2.14+z),.055,.022,'Glow','Y',8)
            gun(s*.39,.51,1.88,1.15,.05)
        rod('Radar mast',(0,-.64,1.85),(0,-.64,2.93),.105,'Metal')
        box('Phased array radar',(0,-.65,3.02),(1.52,.18,.71),'Secondary',.055,rot=(math.radians(-12),0,0))
        for x in [-.52,-.26,0,.26,.52]:glowline((x,-.529,3.025),(.10,.02,.45))

def drone():
    hull('UAV central body',(0,0,1.20),(1.25,1.75,.55),'Armor',.72)
    hull('Drone sensor chin',(0,.67,1.05),(.55,.49,.37),'Secondary',.66)
    cylinder('Optical lens',(0,.925,1.08),.14,.06,'Glass','Y',12)
    glowline((0,.962,1.09),(.14,.014,.045))
    for s in [-1,1]:
        for sy in [-1,1]:
            x=s*1.25;y=sy*.83
            rod('Drone boom',(s*.42,sy*.35,1.2),(x,y,1.23),.10,'Secondary')
            cylinder('Ducted fan guard',(x,y,1.24),.48,.14,'Secondary',verts=20)
            cylinder('Fan interior',(x,y,1.33),.38,.018,'Rubber',verts=20)
            for r in [0,math.pi/2]:box('Fan blade',(x,y,1.35),(.67,.08,.025),'Metal',.005,rot=(0,0,r))
            cylinder('Fan motor',(x,y,1.36),.085,.12,'Glow',verts=10)
            rod('Landing leg',(s*.42,sy*.41,.99),(s*.72,sy*.66,.16),.045,'Metal',8)
            box('Landing foot',(s*.72,sy*.66,.11),(.13,.37,.08),'Secondary',.02)
    gun(0,.31,.87,.82,.055)
    antenna(0,-.50,1.47,.34)

def aircraft(kind):
    d=F=='Dynasty'
    if kind=='Helicopter':
        hull('Attack helicopter fuselage',(0,.06,1.65),(1.71,4.0,1.31),'Armor',.63)
        hull('Tandem cockpit',(0,1.16,2.13),(1.20,2.16,.96),'Glass',.66)
        box('Cockpit armored spine',(0,1.08,2.63),(.17,1.57,.10),'Armor',.04)
        rod('Tail boom',(0,-1.45,1.86),(0,-4.11,2.06),.22,'Secondary',8)
        prism('Tailplane',[(-1.03,-3.67),(1.03,-3.67),(.58,-4.26),(-.58,-4.26)],2.07,.13,'Armor')
        box('Vertical tailfin',(0,-3.95,2.67),(.12,.84,1.55),'Armor',.05,rot=(math.radians(-15),0,0))
        cylinder('Tail rotor axle',(.22,-4.03,2.57),.11,.48,'Metal','X')
        for r in [0,math.pi/2]:box('Tail rotor',(.50,-4.03,2.57),(.045,1.73,.10),'Metal',.008,rot=(r,0,0))
        cylinder('Main rotor shaft',(0,-.48,2.92),.14,.66,'Metal')
        cylinder('Rotor hub',(0,-.48,3.30),.25,.17,'Secondary',verts=12)
        for a in [0,math.pi/2,math.pi,3*math.pi/2]:
            blade=prism('Main rotor blade',[(.19,-.60),(3.76,-.48),(3.68,-.17),(.26,-.28)],3.37,.045,'Metal')
            # Rotate around rotor hub rather than world origin.
            for v in blade.data.vertices:
                x,y=v.co.x,v.co.y+.48
                v.co.x=x*math.cos(a)-y*math.sin(a)
                v.co.y=x*math.sin(a)+y*math.cos(a)-.48
            blade['articulation']='Rotor';blade['pivot']=(0,-.48,3.30)
        for s in [-1,1]:
            hull('Engine nacelle',(s*.74,-.92,2.29),(.61,1.87,.52),'Secondary',.81)
            cylinder('Turbine exhaust',(s*.74,-1.89,2.32),.21,.12,'Metal','Y',12)
            cylinder('Hot turbine core',(s*.74,-1.96,2.32),.13,.025,'Glow','Y',12)
            prism('Weapon stub wing',[(s*.5,.45),(s*2.05,.02),(s*1.87,-.61),(s*.49,-.64)],1.7,.15,'Secondary')
            cylinder('Rocket pod',(s*1.54,.08,1.43),.27,1.1,'Armor','Y',8)
            for ox,oz in [(0,0),(.13,0),(-.13,0),(0,.13),(0,-.13)]:
                cylinder('Rocket tube',(s*1.54+ox,.64,1.43+oz),.055,.025,'Rubber','Y',8)
            rod('Landing skid',(s*.83,-1.65,.25),(s*.83,1.37,.25),.065,'Metal')
            for y in [-.9,.9]:rod('Skid strut',(s*.55,y,1.16),(s*.83,y,.25),.065,'Metal')
        turret(0,1.40,1.06,.38,d)
        return
    bomber=kind=='Bomber'
    span=4.2 if bomber else 3.25
    length=6.45 if bomber else 6.12
    hull('Aircraft fuselage',(0,0,1.27),(1.58 if bomber else 1.14,length,.76),'Secondary',.58)
    prism('Nose upper armor',[(-.55,1.24),(.55,1.24),(0,3.55)],1.43,.36,'Armor')
    hull('Pilot canopy',(0,1.03,1.78),(.77,1.67,.58),'Glass',.59)
    glowline((0,1.01,2.08),(.057,.73,.018))
    for s in [-1,1]:
        if bomber:
            pts=[(s*.49,2.06),(s*span,-.54),(s*3.62,-1.75),(s*1.72,-2.10),(s*.55,-2.80)]
        else:
            pts=[(s*.34,1.38),(s*span,-1.49),(s*3.06,-2.05),(s*.56,-1.31)]
        prism('Swept main wing',pts,1.31,.21,'Armor')
        prism('Wing panel',[(s*.98,.39),(s*(span-.52),-1.09),(s*(span-.67),-1.30),(s*1.12,-.60)],1.433,.024,'Secondary')
        prism('Luminous wing edge',[(s*1.19,.32),(s*(span-.5),-1.04),(s*(span-.55),-1.105),(s*1.22,.22)],1.456,.016,'Glow')
        if not bomber:
            prism('Swept horizontal stabilizer',[(s*.43,-1.85),(s*1.89,-2.71),(s*1.8,-3.2),(s*.36,-2.71)],1.6,.13,'Secondary')
        engine_x=s*(.65 if bomber else .45)
        cylinder('Jet nacelle',(engine_x,-1.81,1.32),.38 if bomber else .30,2.12,'Secondary','Y',12)
        cylinder('Exhaust shroud',(engine_x,-2.94,1.32),.32 if bomber else .27,.31,'Metal','Y',12)
        cylinder('Ion exhaust',(engine_x,-3.105,1.32),.235 if bomber else .18,.025,'Glow','Y',12)
        fin=prism('Canted vertical stabilizer',[(engine_x-.055,-1.39),(engine_x+.055,-1.39),(engine_x+.055,-2.75),(engine_x-.055,-2.75)],2.0,1.12,'Armor')
        fin.rotation_euler[1]=s*math.radians(18)
        for yy in [-.34,-1.0] if bomber else [-.34]:
            rod('Underwing missile',(s*1.62,yy-.42,.99),(s*1.62,yy+.59,.99),.10,'Secondary',8)
            cylinder('Missile seeker',(s*1.62,yy+.62,.99),.075,.08,'Glow','Y',8)
        # Deployed gear makes each static showroom miniature sit at ground level.
        rod('Main landing strut',(s*.68,-1.03,1.06),(s*.80,-1.03,.30),.057,'Metal',8)
        wheel(s*.80,-1.03,.24,.23)
    rod('Nose landing strut',(0,1.99,1.07),(0,1.99,.28),.05,'Metal',8)
    cylinder('Nose tire',(0,1.99,.22),.2,.18,'Rubber','X',12)
    if bomber:
        box('Bomb bay keel',(0,-.43,.88),(.88,2.32,.21),'Metal',.05)
        for s in [-1,1]:glowline((s*.38,-.43,.765),(.027,1.95,.018))

def reset_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for m in list(bpy.data.materials):bpy.data.materials.remove(m)

def field_details(kind):
    if kind in ['Rifle','Rocket','Engineer','Commando','Drone','Worker']:return
    if kind in ['Tank','Heavy','Artillery','AntiAir']:
        heavy=kind=='Heavy'; w=3.35 if heavy else 2.67; l=5.15 if heavy else 4.4
        for s in [-1,1]:
            box('Rear fuel can',(s*.89,-l*.44,1.39),(.39,.22,.45),'Secondary',.035)
            box('Jerrycan pressed reinforcement',(s*.89,-l*.558,1.39),(.26,.018,.24),'Metal',.015)
            rod('Jerrycan handle',(s*.89-.10,-l*.44,1.68),(s*.89+.1,-l*.44,1.68),.022,'Metal',8)
            for yy in [-.93,-.54]:box('Engine deck access hatch',(s*.65,yy,1.587),(.48,.27,.037),'Secondary',.01)
            cylinder('Tow shackle',(s*.67,l*.472,.96),.10,.07,'Metal','Y',12)
        cylinder('Rear towing cable',(0,-l*.459,1.08),.041,1.74,'Metal','X',10)
        for x in [-.78,-.52,-.26,0,.26,.52,.78]:box('Spare track link',(x,l*.343,1.55),(.22,.31,.085),'Metal',.012)
    elif kind in ['Scout','APC','Support']:
        front=1.72 if kind=='Scout' else 2.37
        width=.92 if kind=='Scout' else 1.19
        rod('Tubular brush guard',(-width,front,1.0),(width,front,1.0),.065,'Metal',12)
        for x in [-width,width]:rod('Brush guard upright',(x,front,.69),(x,front,1.32),.055,'Metal',10)
        box('Front grille',(0,front-.09,1.17),(width*1.5,.04,.32),'Metal',.012)
        for i in range(7):box('Grille slat',(-width*.61+i*width*.203,front-.06,1.17),(.065,.05,.27),'Secondary',.008)
        for s in [-1,1]:
            rod('Rearview mirror stalk',(s*width,front-.7,1.67),(s*(width+.30),front-.69,1.96),.029,'Metal',8)
            box('Rearview mirror',(s*(width+.30),front-.69,1.97),(.17,.09,.23),'Metal',.03)
    elif kind in ['Fighter','Bomber','Helicopter']:
        # Recesses, access panels and non-luminous national markings break up broad surfaces.
        for s in [-1,1]:
            box('Engine intake dark interior',(s*.67,-.49,2.42 if kind=='Helicopter' else 1.52),(.33,.65,.045),'Metal',.025)
            for i in range(4):box('Engine intake louver',(s*.67,-.7+i*.15,2.451 if kind=='Helicopter' else 1.551),(.31,.039,.022),'Secondary',.005)

def make_faction(faction):
    global F,M,PARTS
    F=faction
    reset_scene()
    if F=='Vanguard':
        palette={'Armor':(.62,.56,.40),'Secondary':(.27,.31,.24),'Glow':(.20,.36,.55),'Metal':(.15,.16,.14),'Glass':(.06,.115,.13),'Rubber':(.055,.051,.044),'Skin':(.49,.34,.23)}
    else:
        palette={'Armor':(.32,.38,.22),'Secondary':(.36,.20,.13),'Glow':(.65,.14,.08),'Metal':(.15,.16,.12),'Glass':(.07,.105,.09),'Rubber':(.055,.050,.043),'Skin':(.49,.34,.23)}
    M={k:material(F+'_'+k,c,.55 if k=='Metal' else .03,.23 if k=='Glass' else .79,0) for k,c in palette.items()}
    scene=bpy.context.scene
    scene.unit_settings.system='METRIC'
    scene.unit_settings.scale_length=1
    units=[]
    active_ids = [key for key in IDS if not FILTER or key in FILTER]
    for i,kind in enumerate(active_ids):
        PARTS=[]
        if kind in ['Tank','Heavy']:reference_tank(globals(), kind)
        elif kind in ['Drone','Helicopter','Fighter','Bomber']:reference_aircraft(globals(), kind)
        elif kind in ['Rifle','Rocket','Engineer','Commando']:humanoid(kind)
        elif kind=='Worker':worker()
        elif kind=='Scout':scout()
        elif kind=='APC':apc()
        elif kind=='Support':apc(True)
        elif kind in ['Tank','Heavy','Artillery','AntiAir']:tank(kind)
        elif kind=='Drone':drone()
        else:aircraft(kind)
        if kind not in ['Tank','Heavy','Drone','Helicopter','Fighter','Bomber']:field_details(kind)
        groups={}
        for part in PARTS:groups.setdefault(part.get('articulation','Hull'),[]).append(part)
        assembled=[]
        for group,objects in groups.items():
            pivot=objects[0].get('pivot',(0,0,0))
            bpy.ops.object.select_all(action='DESELECT')
            for part in objects:part.select_set(True)
            bpy.context.view_layer.objects.active=objects[0]
            bpy.ops.object.join()
            part=bpy.context.object
            part.name=F+'_'+kind if group=='Hull' else group
            scene.cursor.location=pivot
            bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
            bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
            triangulate=part.modifiers.new('Explicit engine triangles','TRIANGULATE')
            bpy.ops.object.modifier_apply(modifier=triangulate.name)
            bm=bmesh.new();bm.from_mesh(part.data)
            degenerate=[f for f in bm.faces if f.calc_area()<1e-10]
            if degenerate:bmesh.ops.delete(bm,geom=degenerate,context='FACES_ONLY')
            bm.to_mesh(part.data);bm.free();part.data.update()
            assembled.append(part)
        obj=next(o for o in assembled if o.name==F+'_'+kind)
        # Every asset uses the physical lowest point as zero, avoiding floating wheels.
        minz=min((part.matrix_world@v.co).z for part in assembled for v in part.data.vertices)
        for part in assembled:
            if part==obj:
                for v in part.data.vertices:v.co.z-=minz
                part.data.update()
            else:
                part.location.z-=minz
                part.parent=obj
        bpy.context.view_layer.update()
        points=[part.matrix_world@Vector(corner) for part in assembled for corner in part.bound_box]
        dims=[round(max(p[axis] for p in points)-min(p[axis] for p in points),3) for axis in range(3)]
        tris=sum(len(part.data.polygons) for part in assembled)
        assert tris<90000,(obj.name,tris)
        obj['faction']=F
        obj['unit_id']=kind
        obj['forward_axis']='+Y (Blender); FBX standard -Z forward conversion'
        obj['triangle_count']=tris
        path=os.path.join(OUT,obj.name+'.fbx')
        bpy.ops.object.select_all(action='DESELECT')
        for part in assembled:part.select_set(True)
        bpy.ops.export_scene.fbx(filepath=path,use_selection=True,object_types={'MESH'},global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
        REPORT.append({'name':obj.name,'triangles':tris,'dimensions_m':dims,'articulated_parts':[p.name for p in assembled if p!=obj],'file':os.path.relpath(path,ROOT).replace('\\','/')})
        # Free stable exported articulation names before constructing the next unit.
        for part in assembled:
            if part!=obj:part.name=obj.name+'_'+part.name
        obj.location=(i%4*11,-(i//4)*11,0)
        units.append(obj)
        print('VALIDATED_ASSET',obj.name,tris,dims,flush=True)
    # The editable source includes an organized labeled showroom and a saved camera.
    stage=material(F+'_Stage',(.025,.031,.045),.08,.75)
    textmat=material(F+'_Labels',(.65,.74,.81),0,.7)
    for i,o in enumerate(units):
        bpy.ops.mesh.primitive_cube_add(size=1,location=(o.location.x,o.location.y,-.20))
        p=bpy.context.object;p.name='Display plinth '+active_ids[i];p.dimensions=(10.3,10.3,.25);p.data.materials.append(stage)
        bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
        bpy.ops.object.text_add(location=(o.location.x,o.location.y-4.2,-.055))
        t=bpy.context.object;t.name='Label '+active_ids[i];t.data.body=active_ids[i].upper();t.rotation_euler.z=math.pi;t.data.align_x='CENTER';t.data.size=.52;t.data.extrude=.002;t.data.materials.append(textmat)
    world=bpy.data.worlds.new(F+' Studio') if not bpy.data.worlds else bpy.data.worlds[0]
    scene.world=world;world.use_nodes=True
    world.node_tree.nodes['Background'].inputs['Color'].default_value=(.09,.12,.17,1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value=.65
    bpy.ops.object.light_add(type='AREA',location=(5,4,23))
    bpy.context.object.name='Large softbox';bpy.context.object.data.energy=6000;bpy.context.object.data.shape='DISK';bpy.context.object.data.size=25
    bpy.ops.object.light_add(type='SUN',location=(0,0,12))
    bpy.context.object.rotation_euler=(.45,-.5,-.5);bpy.context.object.data.energy=2.3
    bpy.ops.object.camera_add(location=(49,37,53))
    cam=bpy.context.object;cam.rotation_euler=(Vector((16.5,-16.5,0))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=66;scene.camera=cam
    scene.render.engine='BLENDER_WORKBENCH'
    scene.display.shading.light='STUDIO'
    scene.display.shading.studio_light='paint.sl'
    scene.display.shading.color_type='MATERIAL'
    scene.display.shading.show_shadows=True
    scene.display.shading.show_cavity=True
    scene.display.shading.cavity_type='BOTH'
    scene.display.shading.curvature_ridge_factor=1.3
    scene.display.shading.curvature_valley_factor=1.1
    scene.render.resolution_x=1500;scene.render.resolution_y=1500;scene.render.resolution_percentage=100
    scene.view_settings.view_transform='AgX'
    bpy.ops.object.select_all(action='DESELECT')
    units[0].select_set(True);bpy.context.view_layer.objects.active=units[0]
    source_suffix = '_Reference_Units' if FILTER else '_Units'
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SOURCE,F+source_suffix+'.blend'))
    if '--render' in __import__('sys').argv:
        scene.render.filepath=os.path.join(SOURCE,F+source_suffix+'_Overview.png')
        bpy.ops.render.render(write_still=True)

for faction in FACTIONS:make_faction(faction)
if FILTER or len(FACTIONS) < 2:
    previous_path = os.path.join(SOURCE,'unit_asset_manifest.json')
    if os.path.exists(previous_path):
        with open(previous_path) as prior_file: previous_report = json.load(prior_file)
        replaced = {item['name'] for item in REPORT}
        REPORT = [item for item in previous_report if item['name'] not in replaced] + REPORT
REPORT.sort(key=lambda item: item['name'])
with open(os.path.join(SOURCE,'unit_asset_manifest.json'),'w') as f:json.dump(REPORT,f,indent=2)
notes='# Reference-directed unit art\n\n32 original unit variants across two factions, within the complete 52-asset unit and structure library. All existing IDs remain stable. The supplied visual references guide 12 detailed unit variants: Tank, Heavy, Fighter, Bomber, Helicopter and Drone for both factions.\n\nTanks use broad graphite hulls, layered wedge armor and oversized rectangular railguns. Aircraft use sculpted pearl shells, sweeping segmented wings, dark recessed machinery, black canopies and amber engine accents. Infantry, bulldozers, reconnaissance and other support vehicles retain their distinct military equipment and desert/olive or olive/oxide-red faction palettes. Structures use their own dark industrial and cyan-lit palette, described in `Documentation/MilitaryArtPass.md`.\n\n## Geometry and articulation\n\n- Tanks have individually modeled tread shoes, rubber pads, bolted road wheels, drive sprockets, segmented skirts, access panels, grilles and fasteners. Wedge turrets carry protected optics, smoke launchers, roof weapons and rectangular cannons with open framed muzzles; Heavy uses twin cannons.\n- Aircraft have actual segmented shell and wing geometry around recessed mechanical assemblies. Fighter, Bomber, Helicopter and Drone retain distinct silhouettes and proportions.\n- Tank, Heavy, APC and Scout export a separate `TurretPivot` mesh. Helicopter exports `Rotor`, centered on its main shaft. These local Y-up pivots support runtime mechanical rotation; no skeletal animation clips are supplied.\n- Infantry have human proportions, visible faces, fatigues, webbing, cargo pockets, protective goggles and conventional rifles or shoulder rockets.\n- Workers are tracked construction bulldozers with armored glazing, roll cages, hydraulic blade arms and exhaust stacks.\n- Tire treads, wheel bolts, brush guards, mirrors, intakes and service equipment remain geometry on the other military vehicles.\n\n## Materials\n\n- Tanks: `{Faction}_TankArmor`, `_TankSecondary`, `_TankMetal`, `_TankRubber`, `_TankGlow`, `_TankGlass`.\n- Aircraft: `{Faction}_AircraftArmor`, `_AircraftSecondary`, `_AircraftMetal`, `_AircraftGlass`, `_AircraftGlow`, `_AircraftSensor`.\n- Other military units: `{Faction}_Armor`, `_Secondary`, `_Glow`, `_Metal`, `_Glass`, `_Rubber`, `_Skin`.\n- The original `_Glow` slot represents painted identification. Dedicated tank and aircraft glow slots supply small optics and engine accents. Preserve these separate material settings when remapping in Unity.\n\n## Files and regeneration\n\n- Current Unity assets: `Assets/Armies/Models/Units/{Faction}_{ID}.fbx`.\n- Editable current reference variants: `ArtSource/Units/{Faction}_Reference_Units.blend`. Full unit catalogues retain the other ten types and earlier versions of the replaced models.\n- Complete regeneration: `blender --background --python Tools/Blender/generate_units.py -- --render`.\n- Reference-only regeneration: `blender --background --python Tools/Blender/generate_units.py -- --filter Tank,Heavy,Fighter,Bomber,Helicopter,Drone --render`. This merges the manifest and preserves separate source catalogues.\n- `ref_tanks.py` and `ref_aircraft.py` supply the reference builders. FBX meshes use meters, a ground origin and standard -Z forward / Y-up conversion from Blender +Y nose / +Z up.\n- Faces are explicitly triangulated and zero-area faces removed. The enforced limit is below 90,000 triangles per complete unit. The current maximum is 78,180 triangles (Heavy).\n- All detail is original mesh geometry and material separation. No paid assets, asset downloads, extracted game models, model texture atlases or normal maps are required.\n\n## Gameplay use\n\nAirfields have four fixed-wing parking slots, with queued Fighters and Bombers reserving capacity. Planes park when produced, carry four ammunition charges, and return to land and rearm in eight seconds with normal power. A power shortage slows rearming. Helicopters and drones do not consume fixed-wing slots. Factory and barracks units emerge through their modeled exits and deployment lanes before continuing to rally points.\n\nThis is original art for a playable demo, without a claim of exact C&C Generals reproduction or full feature parity. Current build and gameplay verification results are recorded separately in `Documentation/GameplayVerification.md`.\n\n## Export manifest\n\n| Asset | Triangles | Dimensions (m, Blender X/Y/Z) |\n|---|---:|---|\n'
for r in REPORT:notes+='| '+r['name']+' | '+str(r['triangles'])+' | '+str(r['dimensions_m'])+' |\n'
with open(os.path.join(ROOT,'Documentation/UnitArtNotes.md'),'w',encoding='utf-8') as f:f.write(notes)
print('FINISHED',len(REPORT),'unit assets; maximum triangles',max(r['triangles'] for r in REPORT),flush=True)
