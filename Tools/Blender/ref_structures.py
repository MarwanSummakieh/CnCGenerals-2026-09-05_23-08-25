"""Dense, low-rise industrial architecture based on the user's stacked city reference.

Preserves every stable RTS building identifier. Geometry supplies window grids,
cantilever decks, exposed services, and actual open production bays.
"""
import bpy, math

ANCHORS = {
    'Airfield': {'AircraftPad0': (5,-10.5,.4), 'AircraftPad1': (5,-3.5,.4),
                 'AircraftPad2': (5,3.5,.4), 'AircraftPad3': (5,10.5,.4),
                 'AircraftRunwayStart': (-5.5,-14.5,.4), 'AircraftRunwayEnd': (-5.5,14.5,.4)},
    'Factory': {'ProductionExit': (0,-3.1,.62), 'ProductionLaneEnd': (0,-8.4,.62)},
    'Barracks': {'ProductionExit': (0,-3.8,.62), 'ProductionLaneEnd': (0,-5.5,.62)},
}

def install(g):
    box, slab, beam, cyl = g['box'], g['slab'], g['beam'], g['cyl']
    base, vents, beacon, crate = g['base'], g['vents'], g['beacon'], g['crate']

    def palette(faction):
        warm = faction == 'Dynasty'
        colors = {'Armor':(.078,.072,.068) if warm else (.052,.076,.099),
                  'Secondary':(.23,.20,.17) if warm else (.14,.19,.235),
                  'Glow':(.16,.83,1.0), 'Glass':(.13,.38,.43), 'Metal':(.055,.065,.072)}
        result = {key:g['material'](faction+'_Structure'+key, color,
                    .62 if key=='Metal' else .28, .28 if key=='Glass' else .54,
                    3.2 if key=='Glow' else .65 if key=='Glass' else 0)
                  for key,color in colors.items()}
        result['Accent'] = result['Secondary']
        return result

    def window_grid(x,y,z,w,count=6,rows=2,height=.28):
        box('Continuous recessed window array',(x,y,z),(w,.045,rows*(height+.13)+.09),'Metal',0)
        for row in range(rows):
            for i in range(count):
                # Deliberate dark offices interrupt the grid, like the supplied urban reference.
                if (i+row*3)%7 == 5: continue
                xx=x+(i-(count-1)/2)*w/count
                zz=z+(row-(rows-1)/2)*(height+.13)
                box('Illuminated modular office pane',(xx,y-.037,zz),(w/count-.11,.025,height),
                    'Glow' if (i+row)%5==0 else 'Glass',0)

    def roof(name,x,y,z,w,d):
        slab(name+' cantilever shadow deck',(x,y,z),(w+.9,d+.85),(w+.75,d+.72),.22,'Metal')
        slab(name+' floating armor cornice',(x,y,z+.19),(w+.65,d+.60),(w+.48,d+.43),.17,'Secondary')
        box(name+' cyan fascia ribbon',(x,y-d/2-.36,z+.07),(w*.84,.035,.075),'Glow',0)
        for side in [-1,1]:
            for yy in [-d*.31,d*.31]:
                box(name+' external cantilever bracket',(x+side*(w*.47),y+yy,z-.22),(.16,.31,.58),'Metal',.01)
            box(name+' recessed roof perimeter',(x+side*w*.46,y,z+.34),(.12,d*.88,.10),'Metal',0)
        box(name+' roof cable tray',(x,y+d*.35,z+.35),(w*.73,.21,.13),'Metal',.015)

    def body(name,x,y,z,w,d,h):
        slab(name+' stacked architectural mass',(x,y,z),(w,d),(w-.14,d-.10),h,'Armor')
        for side in [-1,1]:
            box(name+' exterior structural spine',(x+side*(w/2-.17),y,z),(.33,d+.18,h+.11),'Secondary',.025)
            for i in range(max(2,int(d/.75))):
                yy=y-d*.4+i*d*.8/max(1,int(d/.75)-1)
                box(name+' side service fins',(x+side*(w/2+.045),yy,z),(.08,.12,h*.64),'Metal',0)
        window_grid(x,y-d/2-.09,z+h*.19,max(.5,w-.8),max(2,int(w/.65)),2 if h>2.1 else 1,.23)
        if h>3.5:
            roof(name+' intermediate service terrace',x,y,z+.05,w+.20,d+.15)
            window_grid(x,y-d/2-.09,z-h*.20,max(.5,w-.8),max(2,int(w/.65)),2,.20)
        roof(name,x,y,z+h/2+.10,w,d)

    def windows(x,y,z,w,count=4,height=.58):
        window_grid(x,y,z,w,max(count,int(w/.55)),max(1,int(height/.35)),.23)

    def service_pack(x,y,z):
        box('Rooftop service equipment plinth',(x,y,z),(1.45,1.35,.24),'Secondary',.03)
        for side in [-1,1]:
            box('Industrial rooftop chiller',(x+side*.38,y,z+.36),(.56,1.17,.54),'Metal',.02)
            cyl('Circular chiller fan',(x+side*.38,y,z+.65),.24,.045,'Secondary',16)
            for i in range(5):box('Chiller radiator tooth',(x+side*.38,y-.59,z+.14+i*.095),(.49,.035,.045),'Secondary',0)
        for i in range(2):beam('Utility circulation conduit',(x-.30+i*.6,y+.66,z+.05),(x-.30+i*.6,y+1.35,z+.05),.06,'Metal',8)

    def signage(text,x,y,z,w):
        box('Illuminated industrial sign housing',(x,y,z),(w,.12,.50),'Metal',.02)
        box('Sign cyan luminous backing',(x,y-.07,z),(w-.16,.026,.36),'Glow',0)
        bpy.ops.object.text_add(location=(x,y-.10,z))
        o=bpy.context.object;o.rotation_euler=(math.pi/2,0,0)
        o.data.body=text;o.data.align_x='CENTER';o.data.align_y='CENTER';o.data.size=min(.30,w/(len(text)*.7));o.data.extrude=.001
        bpy.ops.object.convert(target='MESH');g['finish'](bpy.context.object,'Facility channel identification','Metal')

    def factory():
        base(15,18)
        # The front is a genuine void, with a visible floor, back wall and roof cranes.
        box('Open workshop interior floor',(0,1.4,.605),(8.4,10,.07),'Metal',0)
        for side in [-1,1]:
            body('Stacked assembly service wing',side*5.54,1.85,2.95,3.45,9.1,4.65)
            box('Open bay reinforced jamb',(side*3.76,-3.10,2.95),(.50,.48,4.75),'Secondary',.04)
            box('Workshop entry luminous jamb',(side*3.46,-3.36,3.01),(.09,.035,4.31),'Glow',0)
            for yy in [-1,1.4,3.8]:
                box('Interior fabrication robot pedestal',(side*2.96,yy,1.07),(.73,.75,.90),'Secondary',.03)
                beam('Articulated fabrication arm',(side*2.96,yy,1.52),(side*2.22,yy,2.28),.11,'Metal',8)
                beam('Fabrication arm tool',(side*2.22,yy,2.28),(side*1.6,yy,1.84),.075,'Secondary',8)
            service_pack(side*5.52,2.2,5.57)
        box('Workshop back wall',(0,6.40,2.89),(14.2,.45,4.65),'Armor',.04)
        roof('Cantilevered factory roof',0,1.75,5.58,14.0,9.4)
        box('Gantry longitudinal beam',(0,2,4.80),(7.45,.47,.46),'Secondary',.035)
        cyl('Suspended assembly crane hook',(0,2,3.95),.20,1.4,'Metal',12)
        for side in [-1,1]:
            # Steel production rails visibly project five meters from the open bay.
            box('Continuous production egress rail',(side*1.82,-1.80,.71),(.17,13.25,.18),'Metal',.015)
            box('Production rail polished running surface',(side*1.82,-1.80,.813),(.075,13.25,.025),'Secondary',0)
        for i in range(22):box('Production rail cross sleeper',(0,-8.15+i*.58,.65),(4.10,.15,.10),'Secondary',0)
        for yy in [-7.8,-5.3,-2.8]:
            for side in [-1,1]:box('Assembly exit lane illumination',(side*2.56,yy,.64),(.10,1.30,.035),'Glow',0)
        signage('VEHICLE ASSEMBLY / 07',0,-3.64,5.43,7.05)
        beacon(-5.6,5.0,5.5,1.1)

    def barracks():
        base(11.4,12)
        body('Stacked garrison accommodation',0,1.30,2.47,8.6,6.5,3.7)
        body('Cantilevered operations annex',2.5,1.7,5.0,3.1,4.1,1.35)
        # Soldier doorway is open and front-facing, with a distinct illuminated approach.
        box('Infantry entry dark recessed alcove',(0,-2.94,1.87),(3.3,1.70,2.55),'Metal',.025)
        for side in [-1,1]:
            box('Infantry entrance buttress',(side*1.95,-3.1,1.75),(.55,2.0,2.25),'Secondary',.045)
            box('Infantry entry light jamb',(side*1.62,-4.07,1.77),(.10,.06,2.18),'Glow',0)
            for yy in [-4.5,-5.2]:box('Soldier approach illuminated bollard',(side*2.09,yy,1.12),(.24,.24,1.0),'Secondary',.02)
            beam('Entrance protective rail',(side*1.64,-4.05,.79),(side*1.64,-5.4,.79),.055,'Metal',8)
        roof('Infantry entrance overhang',0,-3.10,3.15,4.6,2.2)
        box('Infantry unobstructed deployment path',(0,-4.64,.62),(3.0,2.25,.08),'Metal',0)
        for i in range(5):box('Soldier path luminous paver',(0,-3.8-i*.39,.673),(1.7,.11,.025),'Glow',0)
        signage('INFANTRY / 02',0,-4.31,3.38,4.02)
        for x in [-2.3,0]:service_pack(x,2.0,4.53)
        beacon(2.6,2.2,5.90,1.1)

    def airfield():
        # Exactly 24m x 34m. The runway and four numbered pads remain unobstructed.
        slab('Airfield 24 by 34 foundation',(0,0,.17),(24,34),(24,34),.34,'Metal')
        box('Airfield tarmac apron',(0,0,.335),(23.65,33.65,.07),'Secondary',0)
        box('Full length runway',(-5.5,0,.395),(7.0,31.0,.01),'Metal',0)
        for yy in range(-13,15,3):box('Runway dashed centerline',(-5.5,yy,.409),(.16,1.35,.018),'Glow',0)
        for side in [-1,1]:
            box('Runway edge painted line',(-5.5+side*3.18,0,.41),(.09,29.8,.02),'Glass',0)
            for yy in range(-15,16,3):box('Runway edge inset beacon',(-5.5+side*3.37,yy,.45),(.18,.28,.10),'Glow',0)
        for yy in [-14,14]:
            for xx in [-7.9,-7.2,-6.5,-4.5,-3.8,-3.1]:box('Runway threshold piano key',(xx,yy,.411),(.34,1.6,.019),'Glass',0)
        for i,yy in enumerate([-10.5,-3.5,3.5,10.5]):
            box('Aircraft parking pad '+str(i),(5,yy,.385),(8.0,5.55,.03),'Metal',0)
            for side in [-1,1]:
                box('Aircraft pad side boundary',(5+side*3.69,yy,.414),(.10,5.0,.025),'Glow',0)
                box('Aircraft pad end boundary',(5,yy+side*2.51,.414),(7.4,.10,.025),'Glass',0)
            box('Pad aircraft center alignment',(5,yy,.413),(.10,3.9,.022),'Secondary',0)
            box('Pad aircraft cross alignment',(5,yy,.414),(2.30,.10,.025),'Secondary',0)
            box('Runway to pad taxi lane',(-.65,yy,.412),(3.55,.15,.02),'Glass',0)
            body('Pad service module',10.5,yy,1.11,1.5,2.4,1.42)
            box('Aircraft refuelling service arm',(9.67,yy,1.15),(1.05,.20,.17),'Metal',.015)
            # Mesh numerals face upward to identify the four operational parking locations.
            bpy.ops.object.text_add(location=(2.1,yy-1.9,.431));o=bpy.context.object
            o.data.body='0'+str(i+1);o.data.size=.63;o.data.extrude=.001
            bpy.ops.object.convert(target='MESH');g['finish'](bpy.context.object,'Aircraft pad index '+str(i),'Glow')
        body('Flight operations terminal',3.4,15.15,1.70,8.1,2.3,2.45)
        body('Stacked air traffic control',9.57,15.15,3.10,3.2,2.3,5.30)
        roof('Air traffic observation crown',9.57,15.15,5.96,3.4,2.6)
        signage('AIR OPERATIONS',3.4,13.70,2.65,7.0)
        beacon(9.55,15.18,6.2,1.1)

    def field_service_details(ident):
        if ident=='Airfield': return
        sizes={'Command':(11.5,10),'Power':(10,9),'Refinery':(13,11),'Barracks':(11.4,12),'Factory':(15,18),'Tech':(10.8,10),'Turret':(7.4,7.4),'AirDefense':(7.4,7.4),'Superweapon':(13,12)}
        w,d=sizes[ident]
        for side in [-1,1]:
            xx=side*(w*.44)
            beam('Exposed perimeter service pipe',(xx,-d*.26,.83),(xx,d*.36,.83),.075,'Metal',8)
            for yy in [-d*.24,d*.05,d*.32]:
                box('Perimeter junction box',(xx,yy,.86),(.35,.28,.44),'Secondary',.025)
                box('Utility junction cyan status',(xx,yy-.15,.91),(.17,.018,.05),'Glow',0)
        for i in range(3):crate(-w*.37,d*.34-i*.64,.60,.52)
        if ident not in ['Factory','Barracks']:
            signage({'Command':'CENTRAL COMMAND','Power':'GRID // 30','Refinery':'SUPPLY TRANSFER','Tech':'RESEARCH DIVISION','Turret':'SENTRY NETWORK','AirDefense':'AIR DEFENSE','Superweapon':'STRATEGIC CONTROL'}[ident],0,-d*.48,1.45,min(w*.54,6.1))
        if ident in ['Command','Tech','Refinery']:
            service_pack(-2.4 if ident=='Refinery' else 2.3,1.5,{'Command':3.25,'Tech':4.65,'Refinery':4.0}[ident])
        if ident=='Command':
            # The signature broad sign crown directly echoes the reference megastructure.
            roof('Command cantilever sign crown',0,1.0,7.68,6.1,5.0)
            signage('TACTICAL COMMAND',0,-1.63,7.65,5.55)

    g.update(palette=palette,body=body,roof=roof,windows=windows,factory=factory,
             barracks=barracks,airfield=airfield,field_service_details=field_service_details)
    g['BUILDERS']=[g[name] for name in ['command','power','refinery','barracks','factory','airfield','tech','turret','air_defense','superweapon']]

def add_anchors(obj,ident):
    result=[]
    for name,position in ANCHORS.get(ident,{}).items():
        # Blender datablock names are scene-global. Preserve unsuffixed FBX anchor
        # names even when the previously exported barracks used ProductionExit.
        existing=bpy.data.objects.get(name)
        if existing: existing.name=(existing.parent.name+'_' if existing.parent else 'Saved_')+name
        anchor=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(anchor)
        anchor.parent=obj;anchor.location=position;anchor.empty_display_type='PLAIN_AXES';anchor.empty_display_size=.45
        anchor.select_set(True);result.append(anchor)
    return result
