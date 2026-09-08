"""Reference-directed graphite combat vehicles, built as dense batched geometry.

Public entry point: build_tank(globals(), kind), kind Tank or Heavy.
Only two mesh objects are produced: body and articulated turret. All paneling,
tread shoes, grilles, fasteners, optic housings and mechanical recesses are mesh
geometry. The input reference is inspiration; the mesh is original artwork.
"""
import bpy
import math
from mathutils import Vector, Euler


class Geometry:
    def __init__(self, name, palette):
        self.name = name
        self.palette = palette
        self.vertices = []
        self.faces = []
        self.indices = []
        self.smooth = []
        self.keys = list(palette)

    def face(self, points, mat, smooth=False):
        start = len(self.vertices)
        self.vertices.extend(tuple(p) for p in points)
        self.faces.append(tuple(range(start, start+len(points))))
        self.indices.append(self.keys.index(mat))
        self.smooth.append(smooth)

    def polyhedron(self, vertices, faces, mat):
        for face in faces:
            self.face([vertices[i] for i in face], mat)

    def box(self, pos, dims, mat='TankArmor', bevel=.035, rot=None):
        # A complete 26-face chamfered cuboid, built directly without object ops.
        h = [d*.5 for d in dims]
        b = max(0, min(bevel, min(h)*.60))
        rotation = Euler(rot or (0,0,0)).to_matrix()
        def p(v):return rotation @ Vector(v) + Vector(pos)
        for axis in range(3):
            other = [a for a in range(3) if a != axis]
            for side in [-1,1]:
                points = []
                for a,c in [(-1,-1),(1,-1),(1,1),(-1,1)]:
                    co=[0,0,0];co[axis]=side*h[axis]
                    co[other[0]]=a*(h[other[0]]-b);co[other[1]]=c*(h[other[1]]-b)
                    points.append(p(co))
                normal=(points[1]-points[0]).cross(points[2]-points[0])
                if normal.dot(rotation@Vector(tuple(side if k==axis else 0 for k in range(3))))<0:points.reverse()
                self.face(points,mat)
        for axis in range(3):
            u,v=[a for a in range(3) if a!=axis]
            for su in [-1,1]:
                for sv in [-1,1]:
                    points=[]
                    for end,outer in [(-1,u),(1,u),(1,v),(-1,v)]:
                        co=[0,0,0];co[axis]=end*(h[axis]-b)
                        co[u]=su*(h[u]-(0 if outer==u else b))
                        co[v]=sv*(h[v]-(0 if outer==v else b))
                        points.append(p(co))
                    center=sum(points,Vector())*.25-Vector(pos)
                    if (points[1]-points[0]).cross(points[2]-points[0]).dot(center)<0:points.reverse()
                    self.face(points,mat)
        for sx in [-1,1]:
            for sy in [-1,1]:
                for sz in [-1,1]:
                    signs=[sx,sy,sz];points=[]
                    for axis in range(3):points.append(p([signs[k]*(h[k]-(0 if k==axis else b)) for k in range(3)]))
                    if (points[1]-points[0]).cross(points[2]-points[0]).dot(sum(points,Vector())/3-Vector(pos))<0:points.reverse()
                    self.face(points,mat)

    def cylinder(self, pos, radius, depth, mat='TankMetal', axis='Z', n=20, inner=0):
        if axis=='X':rotation=Euler((0,math.pi/2,0)).to_matrix()
        elif axis=='Y':rotation=Euler((math.pi/2,0,0)).to_matrix()
        else:rotation=Euler((0,0,0)).to_matrix()
        def p(r,a,z):return rotation@Vector((math.cos(a)*r,math.sin(a)*r,z))+Vector(pos)
        for i in range(n):
            a=i*math.tau/n;b=(i+1)*math.tau/n
            self.face([p(radius,a,-depth/2),p(radius,b,-depth/2),p(radius,b,depth/2),p(radius,a,depth/2)],mat,True)
            if inner:
                self.face([p(inner,a,depth/2),p(inner,b,depth/2),p(inner,b,-depth/2),p(inner,a,-depth/2)],mat,True)
                for z,sgn in [(-depth/2,-1),(depth/2,1)]:
                    pts=[p(inner,a,z),p(radius,a,z),p(radius,b,z),p(inner,b,z)]
                    if sgn<0:pts.reverse()
                    self.face(pts,mat)
        if not inner:
            self.face([p(radius,i*math.tau/n,depth/2) for i in range(n)],mat)
            self.face([p(radius,i*math.tau/n,-depth/2) for i in reversed(range(n))],mat)

    def rod(self, a,b,radius,mat='TankMetal',n=12):
        direction=Vector(b)-Vector(a)
        rotation=direction.to_track_quat('Z','Y').to_matrix()
        center=(Vector(a)+Vector(b))*.5
        def p(angle,z):return rotation@Vector((math.cos(angle)*radius,math.sin(angle)*radius,z))+center
        for i in range(n):
            aa=i*math.tau/n;bb=(i+1)*math.tau/n
            self.face([p(aa,-direction.length*.5),p(bb,-direction.length*.5),p(bb,direction.length*.5),p(aa,direction.length*.5)],mat,True)
        self.face([p(i*math.tau/n,direction.length*.5) for i in range(n)],mat)
        self.face([p(i*math.tau/n,-direction.length*.5) for i in reversed(range(n))],mat)

    def hull(self,pos,dims,mat='TankArmor',taper=.84,front=.2,slope=0):
        x,y,z=pos;w,l,h=dims
        verts=[(x-w/2,y-l/2,z-h/2),(x+w/2,y-l/2,z-h/2),(x+w/2,y+l/2,z-h/2),(x-w/2,y+l/2,z-h/2),
               (x-w*taper/2,y-l/2+.08,z+h/2),(x+w*taper/2,y-l/2+.08,z+h/2),(x+w*taper/2,y+l/2-front,z+h/2-slope),(x-w*taper/2,y+l/2-front,z+h/2-slope)]
        self.polyhedron(verts,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],mat)

    def bolt(self,x,y,z,axis='Z',r=.035):
        self.cylinder((x,y,z),r,r*.42,'TankMetal',axis,8)
        self.cylinder((x,y,z+r*.23 if axis=='Z' else z),r*.39,r*.12,'TankSecondary',axis,6)

    def panel(self,pos,dims,mat='TankArmor',bolts=True,rot=None):
        x,y,z=pos;w,l,h=dims
        self.box(pos,(w+.036,l+.036,h*.75),'TankSecondary',.022,rot)
        self.box((x,y,z+.025),dims,mat,.026,rot)
        if bolts and not rot:
            for sx in [-1,1]:
                for sy in [-1,1]:self.bolt(x+sx*(w/2-.075),y+sy*(l/2-.075),z+h/2+.039,r=.028)

    def grille(self,x,y,z,w,l,n=10):
        self.box((x,y,z),(w,l,.075),'TankSecondary',.025)
        for i in range(n):
            yy=y-l*.43+i*l*.86/(n-1)
            self.box((x,yy,z+.053),(w*.89,l/(n*2.1),.043),'TankMetal',.01)
        for sx in [-1,1]:
            self.box((x+sx*w*.47,y,z+.06),(.035,l*.95,.043),'TankArmor',.008)

    def object(self):
        mesh=bpy.data.meshes.new(self.name+'_Geometry')
        mesh.from_pydata(self.vertices,[],self.faces);mesh.update()
        obj=bpy.data.objects.new(self.name,mesh);bpy.context.collection.objects.link(obj)
        for material in self.palette.values():mesh.materials.append(material)
        for face,index,smooth in zip(mesh.polygons,self.indices,self.smooth):face.material_index=index;face.use_smooth=smooth
        return obj


def materials(ctx):
    palette={
        'TankArmor':((.115,.125,.13),.52,.36),
        'TankSecondary':((.038,.046,.052),.58,.32),
        'TankMetal':((.225,.24,.255),.82,.25),
        'TankRubber':((.012,.015,.018),.05,.80),
        'TankGlow':((.045,.68,.75) if ctx['F']=='Vanguard' else (.83,.12,.055),.25,.22),
        'TankGlass':((.015,.055,.067),.68,.13),
    }
    result={}
    for key,(color,metal,rough) in palette.items():
        m=ctx['material'](ctx['F']+'_'+key,color,metal,rough,1.6 if key=='TankGlow' else 0)
        ctx['M'][key]=m;result[key]=m
    return result


def track_system(g,w,l,heavy):
    for side in [-1,1]:
        x=side*w*.405;run=l-.92
        g.box((x,0,.59),(.60,l-.58,.69),'TankSecondary',.11)
        n=7 if heavy else 6
        for i in range(n):
            y=-run*.44+i*run*.88/(n-1)
            g.cylinder((x+side*.12,y,.58),.40,.47,'TankRubber','X',28)
            g.cylinder((x+side*.365,y,.58),.331,.065,'TankMetal','X',24,inner=.27)
            g.cylinder((x+side*.385,y,.58),.269,.044,'TankSecondary','X',24)
            g.cylinder((x+side*.422,y,.58),.11,.065,'TankMetal','X',16)
            for a in range(8):
                angle=a*math.tau/8
                g.bolt(x+side*.433,y+math.cos(angle)*.206,.58+math.sin(angle)*.206,'X',.023)
            g.rod((x-side*.2,y-.22,.87),(x-side*.2,y,.56),.075,'TankMetal')
        for end in [-1,1]:
            y=end*run*.5
            g.cylinder((x,y,.58),.43,.60,'TankMetal','X',28)
            g.cylinder((x+side*.31,y,.58),.30,.07,'TankSecondary','X',20,inner=.12)
            for a in range(14):
                angle=a*math.tau/14
                g.box((x,y+math.cos(angle)*.43,.58+math.sin(angle)*.43),(.68,.14,.13),'TankMetal',.013,(-angle,0,0))
        shoe_count=int(run/.19)+1
        for i in range(shoe_count):
            y=-run*.5+i*run/(shoe_count-1)
            for z in [.10,1.06]:
                g.box((x,y,z),(.91,.18,.13),'TankMetal',.017)
                g.box((x,y,z+(-.062 if z<.2 else .062)),(.61,.13,.035),'TankRubber',.008)
                for sx in [-1,1]:g.box((x+sx*.37,y,z+(-.053 if z<.2 else .053)),(.075,.15,.065),'TankSecondary',.009)
        for end in [-1,1]:
            for i in range(11):
                a=-math.pi/2+i*math.pi/10
                y=end*(run/2+math.cos(a)*.48);z=.58+math.sin(a)*.48
                g.box((x,y,z),(.91,.18,.13),'TankMetal',.014,(end*(math.pi/2-a),0,0))
        # Four distinct overlapping side plates leave the lower road wheels visible.
        for i in range(4):
            y=-l*.335+i*l*.22
            g.box((x+side*.48,y,.97),(.16,l*.205,.66),'TankArmor',.054,(0,side*.16,0))
            g.box((x+side*.584,y,.98),(.035,l*.17,.40),'TankSecondary',.012,(0,side*.16,0))
            for yy in [y-l*.078,y+l*.078]:
                g.bolt(x+side*.602,yy,1.19,'X',.039)
                g.box((x+side*.613,yy,.85),(.012,.095,.04),'TankGlow',.006)


def body(g,heavy):
    w=4.05 if heavy else 3.68;l=6.05 if heavy else 5.50
    track_system(g,w,l,heavy)
    g.hull((0,.04,1.0),(w*.89,l*.96,.69),'TankSecondary',.90,.40,.10)
    g.hull((0,.09,1.35),(w*.93,l*.93,.55),'TankArmor',.89,.36,.16)
    for s in [-1,1]:
        x=s*w*.405
        g.hull((x,.03,1.27),(.96,l*1.025,.43),'TankArmor',.92,.34,.12)
        for i in range(6):
            y=-l*.385+i*l*.15
            g.panel((x,y,1.511),(.79,l*.13,.076),bolts=True)
            for slot in range(4):
                yy=y-l*.042+slot*l*.027
                g.box((x,yy,1.584),(.57,.045,.019),'TankSecondary',.008)
                g.box((x,yy+.010,1.593),(.46,.013,.014),'TankMetal',.004)
        g.grille(s*.98,-l*.355,1.617,.72,l*.20,14)
        g.panel((s*.56,l*.318,1.58),(.82,.72,.073))
        for i in range(3):
            y=l*.21+i*.20
            g.panel((s*1.13,y,1.65),(.44,.17,.091),'TankMetal',False)
        # Heavily protected forward optic cluster.
        g.box((s*w*.413,l*.483,1.225),(.53,.31,.25),'TankSecondary',.055)
        for dx in [-.115,.115]:
            xx=s*w*.413+dx
            g.cylinder((xx,l*.514,1.225),.092,.095,'TankMetal','Y',20,inner=.063)
            g.cylinder((xx,l*.526,1.225),.062,.022,'TankGlass','Y',20)
            g.cylinder((xx,l*.54,1.225),.035,.009,'TankGlow','Y',16)
        # Rear drive casing, exhaust and maintenance catches.
        g.box((s*1.13,-l*.485,1.21),(.65,.22,.49),'TankSecondary',.046)
        for dx in [-.19,0,.19]:g.cylinder((s*1.13+dx,-l*.514,1.21),.075,.057,'TankMetal','Y',16,inner=.054)
        for yy in [-l*.33,0,l*.3]:
            g.box((s*w*.529,yy,1.41),(.045,.20,.15),'TankMetal',.016)
            g.box((s*w*.539,yy,1.40),(.021,.12,.054),'TankSecondary',.01)
        g.rod((s*.89,l*.31,1.67),(s*.89,l*.44,1.67),.034,'TankMetal')
        for i in range(5):g.box((s*.89,l*.34+i*.09,1.71),(.30,.038,.025),'TankSecondary',.005)
    # Dense maintenance and access panel layout around the central turret race.
    for x in [-.43,.43]:
        g.grille(x,-l*.28,1.658,.66,.62,10)
        for y in [l*.16,l*.30]:g.panel((x,y,1.637),(.65,.64,.051))
    for x in [-.58,0,.58]:
        g.box((x,l*.434,1.428),(.41,.30,.13),'TankSecondary',.04,(-.12,0,0))
        for dx in [-.11,0,.11]:g.box((x+dx,l*.437,1.508),(.047,.20,.028),'TankMetal',.008)
    g.cylinder((0,-.2,1.65),1.28 if heavy else 1.15,.25,'TankMetal','Z',48)
    g.cylinder((0,-.2,1.80),1.20 if heavy else 1.07,.062,'TankSecondary','Z',48)
    return w,l


def weapon(g,x,z,length=4.25):
    # Oversized rectangular railgun, open rectangular muzzle and inset cooling runs.
    g.box((x,2.54,z),(.44,length,.35),'TankSecondary',.055)
    g.box((x,2.36,z+.18),(.51,length-.37,.12),'TankArmor',.03)
    for s in [-1,1]:
        g.box((x+s*.256,2.34,z+.035),(.075,length-.55,.28),'TankMetal',.02)
        g.box((x+s*.298,2.40,z+.04),(.021,length-.92,.115),'TankSecondary',.008)
        for i in range(13):
            y=.89+i*(length-1.05)/12
            g.box((x+s*.298,y,z+.123),(.03,.115,.042),'TankArmor',.009)
            if i%3==0:g.bolt(x+s*.31,y,z+.015,'X',.019)
    for i in range(17):
        y=.83+i*(length-.90)/16
        g.box((x,y,z+.249),(.22,.055,.017),'TankSecondary',.005)
        g.box((x,y,z+.262),(.17,.015,.008),'TankMetal',.002)
    muzzle=2.54+length*.5
    g.box((x,muzzle-.25,z),(.77,.98,.56),'TankArmor',.072)
    # A genuinely hollow framed aperture assembled from four armor beams.
    for s in [-1,1]:
        g.box((x+s*.31,muzzle+.27,z),(.15,.56,.51),'TankArmor',.039)
        g.box((x,muzzle+.27,z+s*.215),(.54,.56,.12),'TankMetal',.027)
        for i in range(7):
            y=muzzle-.58+i*.093
            g.box((x+s*.392,y,z),(.017,.045,.34),'TankSecondary',.01)
    g.box((x,muzzle+.11,z),(.45,.034,.26),'TankRubber',.015)
    for side in [-1,1]:g.box((x+side*.383,muzzle+.23,z),(.018,.35,.07),'TankSecondary',.007)


def turret(g,heavy,faction):
    z=2.16 if heavy else 2.05
    width=3.10 if heavy else 2.80
    g.cylinder((0,-.20,1.84),1.02,.21,'TankSecondary','Z',48)
    g.hull((0,-.30,z),(width,3.00,.85),'TankSecondary',.88,.48,.28)
    g.hull((0,-.39,z+.17),(width*.90,2.87,.63),'TankArmor',.88,.42,.30)
    g.box((0,.93,z-.015),(1.65 if heavy else .99,.55,.68),'TankSecondary',.12)
    g.box((0,1.19,z+.075),(1.49 if heavy else .81,.21,.52),'TankMetal',.06)
    g.box((0,1.309,z+.075),(1.31 if heavy else .64,.02,.35),'TankRubber',.027)
    if heavy:
        for s in [-1,1]:weapon(g,s*.43,z+.03,4.5)
    else:weapon(g,0,z+.04,4.15)
    for s in [-1,1]:
        # Massive layered cheek armor with an inclined maintenance face.
        g.hull((s*.93,.34,z+.22),(.92,2.17,.74),'TankArmor',.87,.50,.34)
        g.hull((s*1.20,.35,z+.16),(.22,2.05,.53),'TankMetal',.88,.44,.27)
        for i in range(3):
            y=.91-i*.39;zz=z+.478-.214*y
            g.panel((s*.90,y,zz),(.65,.33,.061),'TankArmor',False,(-.21,0,0))
            for q in range(5):g.box((s*.90-.24+q*.12,y,zz+.055),(.050,.20,.016),'TankSecondary',.005,(-.21,0,-.12))
        g.box((s*.54,.49,z+.51),(.26,.23,.10),'TankSecondary',.026)
        g.cylinder((s*.54,.615,z+.51),.057,.041,'TankMetal','Y',18,inner=.038)
        g.cylinder((s*.54,.64,z+.51),.036,.009,'TankGlow','Y',16)
        # Recessed side joint, circumferential housing and exposed cross drive.
        g.cylinder((s*1.43,-.72,z+.13),.37,.24,'TankMetal','X',40,inner=.29)
        g.cylinder((s*1.47,-.72,z+.13),.29,.23,'TankSecondary','X',36)
        g.cylinder((s*1.60,-.72,z+.13),.135,.048,'TankArmor','X',24,inner=.083)
        for i in range(8):
            angle=i*math.tau/8
            g.bolt(s*1.618,-.72+math.cos(angle)*.23,z+.13+math.sin(angle)*.23,'X',.028)
        g.cylinder((s*1.50,-.30,z-.16),.116,.065,'TankMetal','X',24,inner=.072)
        g.cylinder((s*1.542,-.30,z-.16),.068,.017,'TankGlow','X',20)
        # Angled smoke launch tubes, each with a separately modeled dark bore.
        for i in range(4):
            y=-.75-i*.19
            a=(s*1.18,y,z+.48);b=(s*1.74,y+.13,z+.73)
            g.rod(a,b,.094,'TankArmor',20)
            g.rod((s*1.71,y+.123,z+.717),(s*1.754,y+.134,z+.735),.071,'TankRubber',16)
            g.rod((s*1.4,y+.052,z+.576),(s*1.46,y+.065,z+.603),.111,'TankMetal',20)
        g.box((s*.94,-1.55,z+.28),(.70,.21,.56),'TankSecondary',.065)
        g.grille(s*.90,-1.10,z+.663,.69,.73,14)
        for i in range(5):
            yy=-1.72+i*.32
            g.box((s*1.32,yy,z+.06),(.07,.18,.22),'TankSecondary',.016)
        for yy in [-1.41,-.14]:
            g.rod((s*1.34,yy,z+.1),(s*1.48,yy,z+.1),.026,'TankMetal')
    # Roof plane broken into individually inset and fastened access panels.
    for x in [-.36,.36]:
        for y in [-.74,-.19]:g.panel((x,y,z+.586),(.53,.47,.057))
    for x in [-.47,.47]:
        g.box((x,-1.70,z+.29),(.79,.18,.44),'TankArmor',.035)
        for i in range(6):g.box((x-.28+i*.112,-1.797,z+.3),(.041,.022,.28),'TankSecondary',.006)
    g.cylinder((-.54,-.62,z+.665),.30,.095,'TankMetal','Z',32)
    g.cylinder((-.54,-.62,z+.722),.257,.045,'TankArmor','Z',32)
    for i in range(12):
        a=i*math.tau/12;g.bolt(-.54+math.cos(a)*.233,-.62+math.sin(a)*.233,z+.754,r=.017)
    g.cylinder((.45,-.78,z+.701),.22,.14,'TankSecondary','Z',28)
    g.box((.45,-.78,z+.89),(.39,.23,.27),'TankArmor',.054)
    g.box((.45,-.645,z+.90),(.29,.031,.12),'TankGlass',.018)
    for s in [-1,1]:g.box((.45+s*.118,-.622,z+.90),(.041,.008,.056),'TankGlow',.004)
    # Remote roof weapon station with receiver, recoil jacket and open protective shield.
    g.rod((-.54,-.62,z+.75),(-.54,-.62,z+1.10),.045,'TankMetal')
    g.box((-.54,-.45,z+1.14),(.17,.52,.17),'TankSecondary',.028)
    g.rod((-.54,-.23,z+1.15),(-.54,.62,z+1.15),.031,'TankMetal',16)
    for i in range(7):g.cylinder((-.54,.12+i*.052,z+1.15),.042,.018,'TankArmor','Y',12)
    g.box((-.73,-.48,z+1.11),(.16,.20,.17),'TankArmor',.019)
    g.box((-.24,-.65,z+1.15),(.06,.57,.41),'TankArmor',.027,(0,-.18,0))
    for x in [-.93,.86]:
        g.cylinder((x,-1.43,z+.68),.075,.18,'TankMetal','Z',16)
        g.rod((x,-1.43,z+.75),(x,-1.52,z+1.59),.016,'TankMetal',10)
    # Small amber/red status pins stay subordinate to the broad dark armor.
    for x in [-.78,-.66,.66,.78]:
        g.cylinder((x,.0,z+.617),.026,.025,'TankMetal','Z',12)
        g.cylinder((x,.0,z+.636),.017,.007,'TankGlow','Z',10)


def build_tank(ctx,kind):
    palette=materials(ctx);heavy=kind=='Heavy'
    chassis=Geometry('Reference graphite chassis',palette)
    body(chassis,heavy)
    turret_geo=Geometry('Reference graphite turret',palette)
    turret(turret_geo,heavy,ctx['F'])
    body_obj=chassis.object();turret_obj=turret_geo.object()
    turret_obj['articulation']='TurretPivot';turret_obj['pivot']=(0,-.20,1.78)
    ctx['PARTS'].extend([body_obj,turret_obj])
    count=sum(len(face)-2 for builder in [chassis,turret_geo] for face in builder.faces)
    body_obj['reference_detail_triangles']=count
    print('REFERENCE_TANK_GEOMETRY',ctx['F'],kind,count,flush=True)
    return body_obj,turret_obj


def preview():
    import os
    root=os.path.abspath(os.path.join(os.path.dirname(__file__),'../..'))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    def material(name,color,metal,rough,emission):
        m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
        bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(*color,1);bs.inputs['Metallic'].default_value=metal;bs.inputs['Roughness'].default_value=rough
        if emission:bs.inputs['Emission Color'].default_value=(*color,1);bs.inputs['Emission Strength'].default_value=emission
        return m
    ctx={'F':'Vanguard','M':{},'PARTS':[],'material':material}
    build_tank(ctx,'Tank')
    bpy.ops.object.camera_add(location=(9,12,8))
    camera=bpy.context.object;camera.rotation_euler=(Vector((0,1.3,1.3))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=11.2
    scene=bpy.context.scene;scene.camera=camera;scene.render.engine='BLENDER_WORKBENCH'
    scene.display.shading.light='STUDIO';scene.display.shading.studio_light='paint.sl';scene.display.shading.color_type='MATERIAL';scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=True;scene.display.shading.cavity_type='BOTH';scene.display.shading.curvature_ridge_factor=1.4;scene.display.shading.curvature_valley_factor=1.25
    scene.world=bpy.data.worlds.new('Graphite studio');scene.world.color=(.05,.055,.06)
    scene.render.resolution_x=1700;scene.render.resolution_y=1250;scene.render.resolution_percentage=100
    scene.render.filepath=os.path.join(root,'ArtSource/Units/Reference_Tank_Preview.png')
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(root,'ArtSource/Units/Reference_Tank_Detail.blend'))
    bpy.ops.render.render(write_still=True)


if __name__=='__main__':preview()
