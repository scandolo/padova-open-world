"""Exercise the open world through actual Pipeline input plus compiled state probes.

python3 Tools/verify_world.py --build --measure
Reuses the existing native-build fingerprint, fresh PID, focus, cleanup and report gates.
"""
import argparse,json,math,time
import verify_playable as v

MANIFEST=v.ROOT/'Assets/Art/Landmarks/Landmarks.json'
def place(name):return next(p for p in json.loads(MANIFEST.read_text())['places'] if p['name']==name)

original=v.run_case
def state():return v.call('world_state')
def until(predicate,timeout=15):
    end=time.monotonic()+timeout
    while time.monotonic()<end:
        s=state()
        if not v.EDITOR and not s['focused']:raise RuntimeError('Player lost focus during world input test')
        if predicate(s):return s
        time.sleep(.12)
    raise AssertionError(('World condition timed out',s))
def delta(a,b):return math.dist(a['position'],b['position'])
def reset():
    v.call('world_fixture','--name','reset');return until(lambda s:s['mode']=='walk' and s['playerEnabled'])
class hold:
    """Keys held for the duration of a with-block and always released."""
    def __init__(self,*keys):self.keys=keys
    def __enter__(self):
        for k in self.keys:v.down(k)
    def __exit__(self,*_):
        for k in reversed(self.keys):v.up(k)

def straight(settle=.9):
    """Driven car at rest at the start of the handling test pad (a flat strip above the city)."""
    info=v.call('activity_fixture','--name','pad');time.sleep(settle)
    s=state();assert s['carGrounded'] and s['carSpeed']<.6 and s['carUp']>.97,('Car did not settle on its wheels',s)
    return info,s

def drive_case():
    """Driving feel by measurement: launch, top speed, braking, turning circle, handbrake slide and recovery, flip reset, crash feedback."""
    reset();v.call('activity_fixture','--name','calm')
    try:return drive_measurements()
    finally:v.call('activity_fixture','--name','busy')

def drive_measurements():
    enter_car();out={}
    info,start=straight();length=info['length'];out.update(vehicle=start['vehicle'])
    trace=[]
    with hold('UpArrow'):
        end=time.monotonic()+30
        while time.monotonic()<end:
            s=state();trace.append([round(s['time']-start['time'],2),round(s['carForward']*3.6,1)])
            if math.dist(s['drivenPosition'],start['drivenPosition'])>length-75:break
            if s['time']-start['time']>6 and s['peakSpeed']-s['carForward']<.02 and len(trace)>20 and trace[-1][1]-trace[-12][1]<.25:break # plateau: top speed reached
    fast=state();out.update(zeroToHundredSeconds=round(fast['zeroToHundred'],2),topKmh=round(fast['peakSpeed']*3.6,1),speedTrace=trace[::3])
    assert fast['peakSpeed']>20,fast
    # Brakes: deceleration between two samples in the middle of a full stop.
    with hold('DownArrow'):
        a=until(lambda s:s['carForward']<fast['carForward']*.85,6);b=until(lambda s:s['carForward']<max(3,a['carForward']*.35),6)
    out['brakeMps2']=round((a['carForward']-b['carForward'])/max(1e-3,b['time']-a['time']),1)
    assert out['brakeMps2']>12,out
    with hold('Space'):stopped=until(lambda s:s['carSpeed']<.3,6)
    out['runMetres']=round(math.dist(stopped['drivenPosition'],start['drivenPosition']),1)
    # Turbo top speed on the same straight.
    info,start=straight()
    with hold('LeftShift','UpArrow'):
        boosted=until(lambda s:s['boosting'],3);time.sleep(2.6)
    out['turboKmhAfter2_6s']=round(state()['peakSpeed']*3.6,1)
    with hold('Space'):until(lambda s:s['carSpeed']<.3,8)
    # Turning circle at parking speed: radius = speed / yaw rate while holding full lock from rest.
    info,start=straight();radii=[]
    with hold('UpArrow','RightArrow'):
        end=time.monotonic()+2.2
        while time.monotonic()<end:
            s=state()
            if 1.5<s['carForward']<5 and s['carYawRate']>.4:radii.append(s['carForward']/s['carYawRate'])
    assert radii,'No steering response at low speed'
    out['turningCircleMetres']=round(2*min(radii),1);assert 4<out['turningCircleMetres']<11,out
    with hold('Space'):until(lambda s:s['carSpeed']<.3,6)
    # Handbrake: the tail comes round, then the car grips again by itself.
    info,start=straight()
    with hold('UpArrow'):until(lambda s:s['carForward']>15,10)
    slip=0;yaw=0
    with hold('RightArrow','Space'):
        end=time.monotonic()+.9
        while time.monotonic()<end:
            s=state();slip=max(slip,s['carSlip']);yaw=max(yaw,abs(s['carYawRate']))
    out.update(handbrakeSlip=round(slip,2),handbrakeYawRate=round(yaw,2));assert slip>.3 and yaw>1,out
    recovered=until(lambda s:s['carSlip']<.05 and abs(s['carYawRate'])<.25,5)
    turned=(recovered['drivenHeading']+180)%360-180
    out['handbrakeTurnDegrees']=round(turned,1);assert recovered['carUp']>.9 and 25<turned<160 and recovered['carGrounded'],recovered
    with hold('Space'):until(lambda s:s['carSpeed']<.3,6)
    # Crash: a wall at speed jolts the camera once and counts one impact.
    v.call('activity_fixture','--name','wall','--distance',60);time.sleep(.9);before=state()
    with hold('UpArrow'):hit=until(lambda s:s['carImpacts']>before['carImpacts'],12)
    time.sleep(.4);after=state()
    out.update(crashClosingKmh=round(hit['lastImpact']*3.6,1),cameraShakeAtImpact=round(hit['cameraShake'],2))
    assert after['crashJolts']>before['crashJolts'] and after['timeScale']==1 and hit['lastImpact']>8,after
    # R from anywhere (here: 400 m up on the pad) puts the car back on the nearest lane in the street.
    v.press('R');street=until(lambda s:s['carGrounded'] and s['drivenPosition'][1]<100 and 'Back on the road' in s['notice'],6)
    # Upside down: the game notices after three seconds, offers R, and R sets the car on its wheels again.
    time.sleep(.5);v.call('activity_fixture','--name','flip')
    stuck=until(lambda s:s['carStuck'],9);assert stuck['carUp']<.3,stuck
    out['stuckPromptScreenshot']=v.capture('world/drive-stuck-native.png')
    v.press('R');upright=until(lambda s:s['carUp']>.97 and s['carGrounded'] and not s['carStuck'],5)
    assert math.dist(upright['drivenPosition'],stuck['drivenPosition'])<40,(stuck,upright)
    with hold('UpArrow'):until(lambda s:s['carForward']>6,6)
    time.sleep(.4);out['screenshot']=v.capture('world/drive-native.png')
    with hold('Space'):until(lambda s:s['carSpeed']<.3,6)
    v.press('E');until(lambda s:s['mode']=='walk',6)
    return out

def run_case(name):
    if name in ['traffic','taxi','race','police']:return activity_case(name)
    if name=='drive':return drive_case()
    if name=='night':return night_case()
    if name not in ['map','car','flight','crowd','quiet_flight','santo','prato','landmarks']:
        reset();return original(name) # Leave any car or plane first: the walking cases need the character.
    reset()
    if name=='quiet_flight':
        v.press('F');until(lambda s:s['mode']=='fly' and s['altitude']>100)
        time.sleep(22) # No polling or capture during this independent performance window.
        performance=v.call('runtime_status').get('Performance');end=state()
        assert end['mode']=='fly' and end['planeTravel']>500,end
        v.press('F');until(lambda s:s['mode']=='walk')
        return {'performance':performance,'travelMetres':end['planeTravel'],'quietSeconds':22}
    if name=='map':
        before=state();v.press('M');opened=until(lambda s:s['mapOpen'])
        v.down('UpArrow')
        try:time.sleep(.5)
        finally:v.up('UpArrow')
        assert delta(before,state())<.05,'Map must pause movement'
        px=opened['screenWidth']*900/1600;py=opened['screenHeight']*.5
        v.call('simulate_pointer','--x',px,'--y',py,'--action','down');time.sleep(.12)
        v.call('simulate_pointer','--x',px,'--y',py,'--action','up')
        marked=until(lambda s:s['HasWaypoint'])
        expected=opened['mapCentre'][0]+(900-800)*opened['mapSpan']/1120
        assert abs(marked['waypoint'][0]-expected)<2,(marked,expected)
        screenshot=v.capture('world/map-native.png');v.press('M');until(lambda s:not s['mapOpen'])
        return {'waypoint':marked['waypoint'],'screenshot':screenshot,'pausedMovement':True}
    if name=='car':
        v.call('world_fixture','--name','car');near=until(lambda s:s['nearby']!='')
        v.press('E');start=until(lambda s:s['mode']=='drive')
        assert not start['playerEnabled'] and not start['colliderEnabled'],start
        v.down('UpArrow')
        try:moving=until(lambda s:s['carSpeed']>3 and delta(start,s)>4)
        finally:v.up('UpArrow')
        v.down('UpArrow');v.down('RightArrow')
        try:steered=until(lambda s:abs(s['carHeading']-moving['carHeading'])>8,8)
        finally:v.up('UpArrow');v.up('RightArrow')
        v.down('Space')
        try:stopped=until(lambda s:s['carSpeed']<.3)
        finally:v.up('Space')
        screenshot=v.capture('world/car-native.png');v.press('E');end=until(lambda s:s['mode']=='walk' and s['playerEnabled'])
        assert math.dist(end['player'],stopped['position'])<4,end
        return {'speedMps':moving['carSpeed'],'distance':delta(start,stopped),'steeringDegrees':abs(steered['carHeading']-moving['carHeading']),'exitPosition':end['player'],'screenshot':screenshot}
    if name=='flight':
        before=state();v.press('F');start=until(lambda s:s['mode']=='fly' and s['altitude']>100)
        v.down('UpArrow');v.down('RightArrow')
        try:moving=until(lambda s:s['altitude']>start['altitude']+12 and delta(start,s)>40)
        finally:v.up('UpArrow');v.up('RightArrow')
        assert abs(moving['planeHeading']-start['planeHeading'])>10,moving
        time.sleep(2.5) # Let the chase camera settle and sample the wider airborne view.
        performance=[]
        if not v.EDITOR:
            for _ in range(3):
                time.sleep(2);performance.append(v.call('runtime_status').get('Performance'))
        # PNG capture stalls a frame on some Metal drivers. Measure before capture.
        screenshot=v.capture('world/flight-native.png')
        v.press('M');until(lambda s:s['mapOpen']);paused=state();time.sleep(.4);assert delta(paused,state())<.05;v.press('M')
        v.press('F');end=until(lambda s:s['mode']=='walk' and s['playerEnabled'])
        assert math.dist(end['player'],before['player'])<.2,(before,end)
        return {'altitude':moving['altitude'],'travelMetres':moving['planeTravel'],'safeReturn':True,'screenshot':screenshot,'flightPerformance':performance}
    if name=='santo':
        # Piazza del Santo discovery point (Assets/Art/Landmarks/Landmarks.json), facing the facade.
        before=state();p=place("Basilica di Sant'Antonio")
        v.call('world_fixture','--name','at','--x',p['x'],'--z',p['z'],'--yaw',71.6)
        arrived=until(lambda s:s['Discoveries']>before['Discoveries'])
        v.down('UpArrow')
        try:walked=until(lambda s:delta(arrived,s)>4,8)
        finally:v.up('UpArrow')
        time.sleep(.6);screenshot=v.capture('world/santo-native.png')
        assert walked['player'][1]>-2.0,walked
        return {'discovered':arrived['Discoveries'],'walkedMetres':round(delta(arrived,walked),2),'screenshot':screenshot}
    if name=='prato':
        # Walk from the outer ring over the western bridge (DBT PONTE:421) onto Isola Memmia.
        v.call('world_fixture','--name','at','--x',129.9,'--z',-1041.7,'--yaw',75.7)
        start=until(lambda s:True);lowest=start['player'][1]
        v.down('UpArrow')
        try:
            end=time.monotonic()+14
            while time.monotonic()<end:
                s=state();lowest=min(lowest,s['player'][1])
                if (s['player'][0]-start['player'][0])*.969+(s['player'][2]-start['player'][2])*.247>24:break
                time.sleep(.08)
        finally:v.up('UpArrow')
        crossed=(s['player'][0]-start['player'][0])*.969+(s['player'][2]-start['player'][2])*.247
        assert crossed>24,('Did not cross the bridge',start,s)
        assert lowest>-3.2,('Fell into the canal (surveyed water level -3.695)',lowest)
        time.sleep(.6);screenshot=v.capture('world/prato-native.png')
        return {'crossedMetres':round(crossed,2),'lowestY':round(lowest,3),'island':s['player'],'screenshot':screenshot}
    if name=='landmarks':
        # Every Blender landmark in the manifest: standing at its discovery point is grounded and discovers it.
        found=[]
        for p in json.loads(MANIFEST.read_text())['places']:
            before=state();v.call('world_fixture','--name','at','--x',p['x'],'--z',p['z'],'--yaw',0)
            s=until(lambda s:s['Discoveries']>before['Discoveries'])
            assert math.hypot(s['player'][0]-p['x'],s['player'][2]-p['z'])<3,(p,s['player'])
            found.append(p['name'])
        return {'discovered':found}
    if name=='crowd':
        end=until(lambda s:s['npcsMoved']>=20,30)
        assert end['npcs']>=28 and end['invalidNav']==0,end
        assert end['worldBuildings']>19000,end
        return {'count':end['npcs'],'moving':end['npcsMoved'],'distanceMetres':end['npcTravel'],'invalidNavigation':end['invalidNav']}

def wait(fetch,predicate,timeout=15):
    end=time.monotonic()+timeout
    while True:
        s=fetch()
        if predicate(s):return s
        if time.monotonic()>end:raise AssertionError(('Condition timed out',s))
        time.sleep(.1)

def night_case():
    # The clock, the lights and the sound: T skips ahead, headlights follow the dark, the engine works through its gears, Q is the horn.
    clock=lambda:v.call('city_time')
    reset();start=clock()
    assert start['ready'] and start['sound'] and start['night']==0 and not start['headlights'],start
    v.press('T');skipped=wait(clock,lambda c:c['skips']==1 and c['hour']>=13.9)
    v.call('city_time','--hour',22,'--running',0);dark=clock()
    assert dark['night']>.95 and dark['sun']<start['sun']*.25,dark
    enter_car();lit=wait(clock,lambda c:c['headlights'])
    v.down('UpArrow')
    try:revved=wait(clock,lambda c:c['gear']>=1 and c['rpm']>.3,12)
    finally:v.up('UpArrow')
    v.down('Q')
    try:honk=wait(clock,lambda c:c['honks']==1 and c['hornHeld'],4)
    finally:v.up('Q')
    wait(clock,lambda c:not c['hornHeld'],4)
    # A lit suburban street for the picture: lamps, pools of light and the car's own beam.
    moved=v.call('city_drive','--x',1935,'--z',-2417);assert moved['success'],moved
    time.sleep(4);screenshot=v.capture('world/night-native.png')
    v.call('city_time','--hour',10,'--running',1);day=clock()
    assert day['night']==0,day
    wait(clock,lambda c:not c['headlights'],4)
    return {'skippedToHour':skipped['hour'],'nightSun':dark['sun'],'headlights':True,'gear':revved['gear'],'rpm':revved['rpm'],'honks':honk['honks'],'muted':honk['volume']==0,'screenshot':screenshot}

def enter_car():
    v.call('world_fixture','--name','car');until(lambda s:s['nearby']!='')
    v.press('Enter');return until(lambda s:s['mode']=='drive')

def choose(key):
    v.press('J');until(lambda s:s['menu']);v.press(key)

def activity_case(name):
    reset();v.call('activity_fixture','--name','save')
    try:
        if name=='traffic':
            start=until(lambda s:s['trafficMoving']>5)
            moving=until(lambda s:s['trafficTravel']>start['trafficTravel']+20)
            assert moving['traffic']>20,moving
            v.press('M');paused=until(lambda s:s['mapOpen']);time.sleep(.4)
            assert abs(state()['trafficTravel']-paused['trafficTravel'])<.01
            v.press('M');until(lambda s:not s['mapOpen'])
            v.call('activity_fixture','--name','traffic');near=until(lambda s:s['nearbyTraffic'],20)
            v.press('Enter');taken=until(lambda s:s['mode']=='drive')
            assert taken['cars']==near['cars']+1 and taken['traffic']==near['traffic'],taken
            time.sleep(2);screenshot=v.capture('world/traffic-native.png')
            return {'count':moving['traffic'],'moving':moving['trafficMoving'],'travelMetres':moving['trafficTravel']-start['trafficTravel'],'takeover':True,'mapPausesTraffic':True,'screenshot':screenshot}
        if name=='police':
            def steal():
                for _ in range(4):
                    v.call('activity_fixture','--name','traffic')
                    try:until(lambda s:s['nearbyTraffic'],8)
                    except AssertionError:continue
                    v.press('E')
                    try:return until(lambda s:s['mode']=='drive',4)
                    except AssertionError:continue
                raise AssertionError('Could not take a traffic car')
            v.call('activity_fixture','--name','clear_police');until(lambda s:s['wanted']==0)
            until(lambda s:s['trafficMoving']>5,40)   # traffic appears once the streamed streets have colliders
            start=steal();assert start['wanted']==1,start
            # Stay put: a patrol must find the player by road and make the arrest.
            chased=until(lambda s:s['police']>=1 and s['policeMoving']>=1,20)
            busted=until(lambda s:s['busts']==1,90)
            assert busted['wanted']==0 and busted['mode']=='walk',busted
            screenshot=v.capture('world/police-busted-native.png')
            # Second theft: once a patrol closes in, keep getting away (each flee moves the car to the lane farthest from
            # every patrol, standing in for driving) until the evasion timer runs out. A parked car is always found.
            steal();until(lambda s:s['police']>=1 and 0<=s['policeNearest']<110,45)
            end=time.monotonic()+60
            while True:
                v.call('activity_fixture','--name','flee');time.sleep(2);escaped=state()
                if escaped['escapes']==1:break
                if time.monotonic()>end:raise AssertionError(('No escape while fleeing',escaped))
            assert escaped['wanted']==0,escaped
            v.press('E');until(lambda s:s['mode']=='walk',6)
            return {'firstSpawnMetres':chased['policeNearest'],'fineEuros':start['money']-busted['money'],'escaped':True,'screenshot':screenshot,
                    'fixture':'Traffic car placement and the escape teleport are fixtures; police routing, arrest and evasion timers are game logic.'}
        if name=='taxi':
            choose('Digit1');until(lambda s:not s['menu']);assert state()['activity']=='' # Cannot start on foot.
            start=enter_car();choose('Digit1');pickup=until(lambda s:s['phase']=='pickup')
            v.press('M');paused=until(lambda s:s['mapOpen']);time.sleep(.3)
            assert abs(state()['clock']-paused['clock'])<.01
            route_screen=v.capture('world/taxi-map-native.png');v.press('M')
            # Set up the last metres, then drive using real input; game logic performs pickup.
            v.call('activity_fixture','--name','target','--distance',12);time.sleep(.3)
            assert state()['phase']=='pickup'
            v.down('UpArrow')
            try:until(lambda s:s['carSpeed']>2,8)
            finally:v.up('UpArrow')
            until(lambda s:s['phase']=='dropoff' or math.dist(s['position'],s['target'])<9,8)
            v.down('Space')
            try:onboard=until(lambda s:s['phase']=='dropoff')
            finally:v.up('Space')
            assert onboard['money']==start['money'] and onboard['fare']>4,onboard
            v.call('activity_fixture','--name','target');paid=until(lambda s:s['fares']==1)
            assert paid['money']>start['money'] and paid['savedMoney']==paid['money'],paid
            screenshot=v.capture('world/taxi-native.png')
            choose('Digit0');ended=until(lambda s:s['activity']=='')
            time.sleep(.3);assert state()['money']==paid['money'],'Duplicate payout after cancelling'
            # R puts the car back on the nearest lane, upright and at rest, wherever it is.
            v.press('R');back=until(lambda s:s['carSpeed']<.5 and s['carGrounded'] and 'Back on the road' in s['notice'],6)
            time.sleep(.3);assert math.dist(state()['position'],back['position'])<1,'Physics undid the car reset'
            return {'paidEuros':paid['money']-start['money'],'fareMetres':onboard['routeLength'],'savedWallet':True,'onFootRejected':True,'mapPausesTimer':True,'cancelled':True,'screenshot':screenshot,'mapScreenshot':route_screen,'fixture':'Position setup for pickup approach and drop-off; actual input drives pickup approach.'}
        start=enter_car();choose('Digit2');until(lambda s:s['phase']=='start')
        v.call('activity_fixture','--name','target');countdown=until(lambda s:s['phase']=='countdown')
        assert countdown['money']==start['money']
        racing=until(lambda s:s['phase']=='racing');count=racing['checkpoints']
        assert count>10,racing
        for i in range(count):
            v.call('activity_fixture','--name','checkpoint')
            until(lambda s:s['checkpoint']==i+1)
        finished=until(lambda s:s['activity']=='')
        assert finished['money']>start['money'] and finished['bestRace']>0,finished
        wallet=finished['money'];time.sleep(.3);assert state()['money']==wallet
        screenshot=v.capture('world/race-native.png')
        return {'checkpoints':count,'rewardEuros':wallet-start['money'],'bestSeconds':finished['bestRace'],'singlePayout':True,'screenshot':screenshot,'fixture':'Position setup at each checkpoint; verifies order, countdown, completion and reward, not a full driven route.'}
    finally:v.call('activity_fixture','--name','restore')

v.run_case=run_case
if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--build',action='store_true');p.add_argument('--editor',action='store_true');p.add_argument('--measure',action='store_true');p.add_argument('--ignore-focus',action='store_true',help='QA only: keep accepting input if another app takes focus');p.add_argument('--headless',action='store_true',help='Launch the native Player hidden in the background: full rendering and HUD, no visible window, no focus change');p.add_argument('--case',default='animation,controls,locomotion,jump,collision,recovery,map,car,drive,flight,crowd,traffic,taxi,race,police,night,landmarks,santo,prato,visual')
    args=p.parse_args()
    if args.editor:
        v.EDITOR=True
        try:v.call('world_fixture','--name','reset')
        except RuntimeError:pass
    v.main(args)
