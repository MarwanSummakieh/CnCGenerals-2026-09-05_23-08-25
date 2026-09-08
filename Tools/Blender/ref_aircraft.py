"""Original sculpted aircraft inspired by the user-supplied pearl/obsidian reference.

Adapter: build_aircraft(globals(), kind) from generate_units.py. Coordinates are
metres, +Y forward and +Z up. Meshes are batched by articulation for fast export.
"""
import math
import bpy
from mathutils import Vector

TAU = math.tau


def _frame(axis):
    axis = Vector(axis).normalized()
    u = axis.cross(Vector((0, 1, 0)) if abs(axis.y) < .9 else Vector((1, 0, 0))).normalized()
    return axis, u, axis.cross(u).normalized()


class AirMesh:
    def __init__(self, ctx, kind):
        self.ctx, self.kind = ctx, kind
        self.data = {}
        self.scale = .49 if kind == 'Drone' else 1.0
        self.materials = []
        self.keys = ['AircraftArmor', 'AircraftSecondary', 'AircraftMetal', 'AircraftGlass', 'AircraftGlow', 'AircraftSensor']
        dynasty = ctx['F'] == 'Dynasty'
        colors = [( .82,.81,.77) if dynasty else (.78,.83,.87), (.32,.39,.43),
                  (.025,.038,.045), (.006,.025,.04), (1,.33,.055), (.07,.7,1)]
        for key, color, metal, rough, emission in zip(self.keys, colors,
                [.48,.62,.85,.7,.5,.3], [.24,.32,.29,.085,.2,.16], [0,0,0,0,4,3]):
            name = ctx['F'] + '_' + key
            material = bpy.data.materials.get(name)
            if material is None:
                material = ctx['material'](name, color, metal, rough, emission)
                bs = material.node_tree.nodes.get('Principled BSDF')
                if 'Coat Weight' in bs.inputs: bs.inputs['Coat Weight'].default_value = .5 if key in ['AircraftArmor','AircraftGlass'] else .15
                if 'Coat Roughness' in bs.inputs: bs.inputs['Coat Roughness'].default_value = .16
            ctx['M'][key] = material
            self.materials.append(material)

    def add(self, vertices, faces, mat=0, smooth=False, group='Hull'):
        data = self.data.setdefault(group, [[], [], [], []])
        offset = len(data[0])
        data[0].extend(tuple(Vector(p) * self.scale) for p in vertices)
        data[1].extend(tuple(i + offset for i in f) for f in faces)
        data[2].extend([mat] * len(faces)); data[3].extend([smooth] * len(faces))

    def box(self, c, size, mat=2, bevel=.025, rotation=0, group='Hull'):
        # A real three-way chamfer: 6 broad faces, 12 bevel faces and 8 corners.
        half = [v*.5 for v in size]; b = min(bevel, min(half)*.65)
        verts=[]; lookup={}
        for axis in range(3):
            for sign in [-1,1]:
                for a in [-1,1]:
                    for d in [-1,1]:
                        p=[0.,0.,0.]; p[axis]=sign*half[axis]
                        other=[j for j in range(3) if j != axis]
                        p[other[0]]=a*(half[other[0]]-b); p[other[1]]=d*(half[other[1]]-b)
                        lookup[(axis,sign,a,d)]=len(verts); verts.append(p)
        faces=[]
        for axis in range(3):
            for sign in [-1,1]:
                faces.append(tuple(lookup[(axis,sign,a,d)] for a,d in [(-1,-1),(1,-1),(1,1),(-1,1)]))
        def ix(axis, signs):
            other=[j for j in range(3) if j != axis]
            return lookup[(axis, signs[axis], signs[other[0]], signs[other[1]])]
        for axis in range(3):
            for other in range(axis+1,3):
                last=3-axis-other
                for sa in [-1,1]:
                    for sb in [-1,1]:
                        s=[1,1,1];s[axis]=sa;s[other]=sb;s[last]=-1
                        a,b0=ix(axis,s),ix(other,s);s[last]=1
                        faces.append((a,b0,ix(other,s),ix(axis,s)))
        for x in [-1,1]:
            for y in [-1,1]:
                for z in [-1,1]: faces.append(tuple(ix(a,[x,y,z]) for a in range(3)))
        cosine,sine=math.cos(rotation),math.sin(rotation)
        points=[(c[0]+p[0]*cosine-p[1]*sine,c[1]+p[0]*sine+p[1]*cosine,c[2]+p[2]) for p in verts]
        self.add(points,faces,mat,False,group)

    def tube(self, center, axis, profile, mat=2, segments=40, group='Hull'):
        axis,u,v=_frame(axis);center=Vector(center);vertices=[]
        for along,radius in profile:
            for j in range(segments):
                angle=j*TAU/segments
                vertices.append(center+axis*along+(u*math.cos(angle)+v*math.sin(angle))*radius)
        faces=[]
        for ring in range(len(profile)-1):
            for j in range(segments):
                k=(j+1)%segments;a=ring*segments
                faces.append((a+j,a+k,a+k+segments,a+j+segments))
        self.add(vertices,faces,mat,True,group)

    def rod(self, a,b,radius,mat=2,segments=12,group='Hull'):
        direction=Vector(b)-Vector(a)
        self.tube(a,direction,[(0,.001),(0,radius),(direction.length,radius),(direction.length,.001)],mat,segments,group)

    def torus(self,center,axis,radius,wire,mat=2,segments=48,rings=10,group='Hull'):
        axis,u,v=_frame(axis);center=Vector(center);vertices=[];faces=[]
        for i in range(segments):
            angle=i*TAU/segments;out=u*math.cos(angle)+v*math.sin(angle)
            for j in range(rings):
                t=j*TAU/rings;vertices.append(center+out*(radius+wire*math.cos(t))+axis*wire*math.sin(t))
        for i in range(segments):
            for j in range(rings):
                faces.append((i*rings+j,((i+1)%segments)*rings+j,((i+1)%segments)*rings+(j+1)%rings,i*rings+(j+1)%rings))
        self.add(vertices,faces,mat,True,group)

    def loft(self,sections,mat=0,sides=48,steps=4,group='Hull'):
        # Sections: y, centre x, centre z, half width, half height.
        points=[]
        for i in range(len(sections)-1):
            for k in range(steps):
                t=k/steps;t=t*t*(3-2*t)
                points.append([a+(b-a)*t for a,b in zip(sections[i],sections[i+1])])
        points.append(sections[-1]);vertices=[];faces=[]
        for y,x,z,w,h in points:
            for j in range(sides):
                a=j*TAU/sides
                vertices.append((x+w*math.cos(a),y,z+h*math.sin(a)))
        for i in range(len(points)-1):
            for j in range(sides):
                k=(j+1)%sides;a=i*sides
                faces.append((a+j,a+k,a+k+sides,a+j+sides))
        faces += [tuple(reversed(range(sides))),tuple(range((len(points)-1)*sides,len(points)*sides))]
        self.add(vertices,faces,mat,True,group)

    def shell(self,outline,z,thickness,mat=0,group='Hull',subdiv=4):
        # Catmull-Rom outline, with a softly inflated closed airfoil cross-section.
        boundary=[];n=len(outline)
        for i in range(n):
            p0,p1,p2,p3=[Vector(outline[k%n]) for k in [i-1,i,i+1,i+2]]
            for step in range(subdiv):
                t=step/subdiv
                boundary.append((2*p1+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t)*.5)
        center=sum(boundary,Vector((0,0)))/len(boundary);count=len(boundary)
        vertices=[];faces=[];radii=[.03,.2,.4,.6,.78,.9,1,.9,.78,.6,.4,.2,.03]
        for ring,r in enumerate(radii):
            height=math.sqrt(max(0,1-r*r))*thickness*(1 if ring<=6 else -.44)
            for p in boundary:
                q=center+(p-center)*r;vertices.append((q.x,q.y,z+height))
        for ring in range(len(radii)-1):
            for j in range(count):
                k=(j+1)%count;a=ring*count
                faces.append((a+j,a+k,a+k+count,a+j+count))
        faces += [tuple(reversed(range(count))),tuple(range((len(radii)-1)*count,len(radii)*count))]
        self.add(vertices,faces,mat,True,group)

    def finish(self):
        for group,(vertices,faces,materials,smoothing) in self.data.items():
            mesh=bpy.data.meshes.new('Reference sculpted '+self.kind+' '+group)
            mesh.from_pydata(vertices,[],faces);mesh.update()
            for material in self.materials:mesh.materials.append(material)
            for face,material,smooth in zip(mesh.polygons,materials,smoothing):
                face.material_index=material;face.use_smooth=smooth
            # Recalculate winding so mirrored aerofoils and chamfer corners face outward.
            import bmesh
            bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
            obj=bpy.data.objects.new('Sculpted '+self.kind+' '+group,mesh);bpy.context.collection.objects.link(obj)
            if group=='Rotor':obj['articulation']='Rotor';obj['pivot']=(0,-.7*self.scale,3.45*self.scale)
            self.ctx['PARTS'].append(obj)


def _engine(m,x,y,z,radius=.55,vertical=False):
    axis=(0,0,-1) if vertical else (0,-1,0)
    m.tube((x,y,z),axis,[(-.78,radius*.62),(-.58,radius),(.27,radius),(.38,radius*.93),(.38,radius*.74),(-.05,radius*.69)],2,48)
    m.torus((x,y-.38 if not vertical else y,z-.38 if vertical else z),axis,radius*.86,.055,1,48,8)
    m.torus((x,y-.395 if not vertical else y,z-.395 if vertical else z),axis,radius*.69,.034,4,48,8)
    m.tube((x,y,z),axis,[(.12,.002),(.12,radius*.6),(.15,radius*.6),(.15,.002)],4,40)
    _,u,v=_frame(axis)
    for i in range(18):
        a=i*TAU/18;radial=u*math.cos(a)+v*math.sin(a);c=Vector((x,y,z))+radial*radius*.84
        m.rod(c+Vector(axis)*-.23,c+Vector(axis)*.26,.035,1,8)
    for i in range(12):
        a=i*TAU/12;out=u*math.cos(a)+v*math.sin(a)
        c=Vector((x,y,z))+Vector(axis)*.2+out*radius*.37
        m.rod(c-out*.12,c+out*.13,.024,2,6)


def build_aircraft(ctx,kind):
    if kind not in ['Fighter','Bomber','Helicopter','Drone']:
        raise ValueError('Unsupported reference aircraft: '+kind)
    m=AirMesh(ctx,kind);dynasty=ctx['F']=='Dynasty';bomber=kind=='Bomber';helicopter=kind=='Helicopter';drone=kind=='Drone'
    span=5.4 if bomber else 3.35 if helicopter else 4.65
    if dynasty:span*=1.06
    # The dark structural spine remains visible in the seams between floating pearl armor.
    m.loft([(-3.2,0,1.45,.18,.18),(-2.6,0,1.5,.8,.48),(-1.1,0,1.58,1.15,.61),
            (.7,0,1.61,1.1,.63),(2.3,0,1.48,.68,.52),(3.55,0,1.32,.12,.16),(3.72,0,1.3,.025,.025)],2,48,4)
    # Continuous recessed panoramic canopy with an asymmetric narrow sensor spine.
    m.loft([(-1.75,0,2.0,.09,.045),(-1.05,0,2.16,.39,.17),(.65,0,2.20,.47,.23),
            (2.1,0,1.95,.30,.17),(2.85,0,1.60,.075,.07)],3,48,4)
    for side in [-1,1]:
        m.loft([(-2.9,side*.26,1.65,.10,.1),(-1.7,side*.64,1.88,.30,.36),
                (.05,side*.80,1.98,.31,.36),(1.65,side*.57,1.78,.22,.28),
                (3.2,side*.19,1.41,.10,.11),(3.57,side*.075,1.31,.025,.035)],0,36,4)
        # Individual shoulder plates follow the curvature; dark channels remain between them.
        for i in range(7):
            y=-2.25+i*.65;x=side*(.83+math.sin((y+2.4)*.62)*.16)
            m.shell([(x-side*.19,y-.28),(x+side*.30,y-.30),(x+side*.38,y+.19),(x-side*.12,y+.29)],
                    2.01 if y<1.5 else 1.85,.047,0,subdiv=3)
        outline=[(side*.94,.90),(side*1.6,1.50),(side*(span*.70),1.96),(side*span,2.45),
                 (side*(span+.12),1.57),(side*(span*.76),.10),(side*(span*.47),-.80),(side*1.1,-1.22)]
        m.shell(outline,1.52,.28,2,subdiv=6)
        inset=[(x*.976,y*.965+.018) for x,y in outline]
        m.shell(inset,1.67,.245,0,subdiv=6)
        # Exposed root nacelles and long intake channels under the crescent wing.
        m.loft([(-2.5,side*1.26,1.40,.30,.30),(-1.6,side*1.48,1.48,.48,.43),
                (.2,side*1.66,1.57,.40,.36),(1.25,side*1.7,1.63,.21,.19)],2,36,4)
        m.shell([(side*1.53,.75),(side*2.05,1.15),(side*2.44,1.12),(side*2.18,.30),(side*1.62,-.30)],1.99,.055,2,subdiv=4)
        for j in range(9):
            yy=.83-j*.13
            m.box((side*(1.86+j*.018),yy,2.065),(.39,.043,.034),1,.012,rotation=side*.38)
            if j in [1,7]:
                m.box((side*(1.86+j*.018),yy+.033,2.074),(.285,.018,.014),4,.005,rotation=side*.38)
        # Wing access panels, engraved joints, actuator rails and very small bolt heads.
        for panel in range(3):
            px=side*(2.3+panel*(span-2.5)/3);py=.95+panel*.27
            panel_z=1.92-panel*.047
            m.shell([(px-side*.21,py-.20),(px+side*.34,py-.14),(px+side*.25,py+.24),(px-side*.20,py+.15)],panel_z,.014,1,subdiv=3)
            m.box((px,py,panel_z+.024),(.075,.31,.022),2,.008,rotation=-side*.25)
            for yy in [-.125,.125]:m.tube((px+side*.20,py+yy,panel_z+.025),(0,0,1),[(0,.023),(.016,.023),(.016,.009)],2,10)
        # Continuous front shoulder light is amber and recessed below the shell.
        for segment in range(8):
            x=side*(1.08+segment*.125);y=.74-segment*.11
            m.rod((x,y,1.55),(x+side*.105,y-.092,1.55),.027,4,8)
        _engine(m,side*1.37,-2.46,1.48,.49)
        if bomber:_engine(m,side*2.29,-1.69,1.40,.43)
        # Outboard fins curl forward and carry exposed mechanical scallops beneath.
        for i in range(8):
            x=side*(span-.26-i*.06);y=1.67-i*.18
            m.box((x,y,1.50),(.18,.11,.18),2,.035,rotation=side*.32)
            m.box((x-side*.10,y+.014,1.43),(.055,.052,.05),4,.012)
        # Small dorsal blue sensor pinlights, deliberately subordinate to engine emission.
        for y in [.1,1.2]:m.tube((side*.54,y,2.22),(0,0,1),[(0,.042),(.022,.042),(.022,.005)],5,16)
        # Swept rear aerofoil, armored power coupling and its inset black service panel.
        m.shell([(side*.55,-1.89),(side*1.30,-2.35),(side*2.71,-2.08),(side*2.98,-2.50),
                 (side*1.65,-3.00),(side*.47,-2.86)],1.77,.13,0,subdiv=4)
        for j in range(7):
            m.box((side*.80,-2.33+j*.083,2.12),(.34,.032,.025),2,.01)
        # Landing gear stays compact, with pearl doors around black hydraulic cylinders.
        for yy in [-1.2,1.65]:
            m.rod((side*.66,yy,1.08),(side*.75,yy,.39),.053,2,12)
            m.rod((side*.74,yy,.44),(side*.9,yy-.12,.25),.045,1,10)
            m.tube((side*.9,yy-.12,.24),(1,0,0),[(-.09,.16),(.09,.16),(.1,.065)],2,24)
            m.box((side*.75,yy,1.0),(.29,.59,.11),0,.035)
    # Belly keel and luminous nose aperture, split by a black instrument recess.
    m.shell([(-.24,2.7),(-.10,3.57),(0,3.76),(.10,3.57),(.24,2.7)],1.14,.105,0,subdiv=5)
    m.loft([(2.70,0,1.31,.19,.047),(3.23,0,1.30,.13,.033),(3.47,0,1.30,.04,.02)],4,24,3)
    # An exposed recessed horseshoe aperture reads from the tactical top/front view.
    nose_aperture=[(-.32,2.53,1.91),(-.28,2.83,1.79),(-.17,3.15,1.64),(0,3.34,1.55),
                   (.17,3.15,1.64),(.28,2.83,1.79),(.32,2.53,1.91)]
    for a,b in zip(nose_aperture,nose_aperture[1:]):
        m.rod(a,b,.032,2,10)
        aa=(a[0],a[1],a[2]+.022);bb=(b[0],b[1],b[2]+.022)
        m.rod(aa,bb,.017,4,10)
    m.box((0,-.23,.96),(.60,2.3,.13),2,.06)
    for i in range(16):m.box((0,-1.2+i*.14,.875),(.44,.054,.031),1,.012)
    if bomber:
        for side in [-1,1]:
            m.box((side*.43,-.3,.83),(.055,2.0,.05),4,.015)
            for y in [-.9,-.35,.2]:m.tube((side*.69,y,1.0),(0,1,0),[(-.22,.12),(.25,.12),(.42,.018)],2,24)
    if helicopter:
        m.loft([(-5.35,0,1.8,.08,.09),(-4.6,0,1.8,.18,.15),(-2.55,0,1.7,.35,.24)],0,32,5)
        m.shell([(-1.2,-4.55),(0,-4.8),(1.2,-4.55),(.69,-5.13),(-.69,-5.13)],1.92,.10,0,subdiv=4)
        m.tube((0,-.7,2.28),(0,0,1),[(0,.20),(.73,.20),(.85,.34),(1.0,.34),(1.12,.15)],2,40)
        m.torus((0,-.7,3.35),(0,0,1),.31,.038,4,40,8)
        for i in range(5):
            a=i*TAU/5;cs,sn=math.cos(a),math.sin(a)
            points=[(.23,-.12),(2.8,-.22),(4.7,.17),(4.85,.38),(2.4,.19),(.28,.12)]
            rotor=[(x*cs-y*sn,-.7+x*sn+y*cs) for x,y in points]
            m.shell(rotor,3.47,.033,1,'Rotor',subdiv=3)
        m.torus((0,-.7,3.47),(0,0,1),.19,.05,0,32,8,'Rotor')
    if drone:
        for side in [-1,1]:
            for yy in [-.85,.85]:
                x=side*2.65
                _engine(m,x,yy,1.35,.44,True)
                m.torus((x,yy,1.87),(0,0,1),.46,.085,0,40,10)
                for blade in range(6):
                    a=blade*TAU/6
                    m.rod((x+math.cos(a)*.09,yy+math.sin(a)*.09,1.90),
                          (x+math.cos(a+.4)*.37,yy+math.sin(a+.4)*.37,1.9),.035,2,6)
    m.finish()
