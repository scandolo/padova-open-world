"""Fast compiled-probe QA: --editor for iteration; --build for the final native gate.
Use --case controls,jump to rerun a failed group, --measure for short counter samples.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import time
import verify_survey as pipeline

ROOT=Path(__file__).resolve().parents[1]
APP=ROOT/'.context/build/PadovaPlayable.app'
pipeline.APP=APP
EDITOR=False
DESCRIPTOR=APP/'.unity-pipeline-runtime-port'

def descriptor():
    try:return json.loads(DESCRIPTOR.read_text())
    except (FileNotFoundError,json.JSONDecodeError):return {}

def close_previous_player():
    info=descriptor()
    if not info.get('pid'):return
    try:
        os.kill(info['pid'],0)
    except ProcessLookupError:return
    pipeline.command('quit',runtime=True)
    end=time.monotonic()+10
    while time.monotonic()<end:
        try:os.kill(info['pid'],0)
        except ProcessLookupError:return
        time.sleep(.1)
    raise RuntimeError('Previous Player did not quit; refusing to test against an ambiguous runtime.')

def fingerprint():
    digest=hashlib.sha256()
    for root in ['Assets','Packages','ProjectSettings']:
        for path in sorted((ROOT/root).rglob('*')):
            if not path.is_file() or 'Settings/Pipeline/' in path.as_posix() or path.name.startswith('InitTestScene'):continue
            digest.update(str(path.relative_to(ROOT)).encode())
            with path.open('rb') as stream:
                for block in iter(lambda:stream.read(1048576),b''):digest.update(block)
    return digest.hexdigest()

def call(name,*args):
    return pipeline.command(name,*args,runtime=not EDITOR)

def state():return call('gameplay_state')

def until(predicate,timeout=8):
    end=time.monotonic()+timeout
    while time.monotonic()<end:
        current=state()
        if not EDITOR and not current.get('focused',False):raise RuntimeError('Player lost focus during input testing; focus it and rerun this case.')
        if predicate(current):return current
        time.sleep(.08)
    raise AssertionError(('State condition timed out',current))

def distance(a,b):return math.hypot(a['position'][0]-b['position'][0],a['position'][2]-b['position'][2])

def reset():
    s=call('gameplay_fixture','--name','reset')
    return until(lambda n:n['frame']>s['frame']+2 and n['grounded'])

def down(k):call('simulate_key','--key',k,'--action','down')
def up(k):call('simulate_key','--key',k,'--action','up')
def press(k):
    before=state()['frame'];down(k)
    try:until(lambda s:s['frame']>before+2)
    finally:up(k)

def capture(name):
    path=ROOT/'.context'/name
    path.parent.mkdir(parents=True,exist_ok=True)
    path.unlink(missing_ok=True)
    call('gameplay_capture','--path',str(path))
    deadline=time.monotonic()+8
    while not path.exists() and time.monotonic()<deadline:time.sleep(.1)
    assert path.exists() and path.stat().st_size>1000,path
    return str(path.relative_to(ROOT))

def travel(metres,sprint=False):
    start=state()
    if sprint:
        if EDITOR:press('Tab')
        else:down('LeftShift')
    down('UpArrow')
    try:
        moving=until(lambda n:distance(start,n)>=metres)
        return start,moving
    finally:
        up('UpArrow')
        if sprint:
            if EDITOR:press('Tab')
            else:up('LeftShift')

def verify_recovery():
    before=reset();call('gameplay_fixture','--name','outside')
    recovered=until(lambda n:n['recoveries']==before['recoveries']+1 and n['grounded'])
    assert distance(before,recovered)<.1,recovered
    return {'name':'Out-of-bounds recovery','passed':True}

def run_case(name):
    start=reset()
    if name=='animation':
        assert 1.4<start['modelHeight']<2.1 and start['modelOffset']<1.3,start
        down('UpArrow')
        try:
            a=until(lambda n:n['speed']>2.8)
            def bone_angle(n):
                dot=abs(sum(x*y for x,y in zip(a['leftFootRotation'],n['leftFootRotation'])))
                return math.degrees(2*math.acos(min(1,dot)))
            # Observe a changed pose over a gait cycle. Two fixed samples can alias
            # the same point on a looping animation even when it is working.
            b=until(lambda n:n['frame']>a['frame']+3 and bone_angle(n)>5,3)
            angle=bone_angle(b)
        finally:up('UpArrow')
        return {'boneRotationDegrees':angle,'height':b['modelHeight']}
    if name=='controls':
        moves={}
        for key in ['UpArrow','DownArrow','LeftArrow','RightArrow']:
            s=reset();down(key)
            try:e=until(lambda n:distance(s,n)>.5)
            finally:up(key)
            delta=[e['position'][i]-s['position'][i] for i in (0,2)]
            expected={'UpArrow':(0,-1),'DownArrow':(0,1),'LeftArrow':(1,0),'RightArrow':(-1,0)}[key]
            assert sum(d*v for d,v in zip(delta,expected))>.4,(key,delta)
            moves[key]=delta
        reset();press('Tab');assert state()['runToggled']
        press('Tab');assert not state()['runToggled']
        # The chase camera swings in behind the direction of travel (original prototype behaviour).
        before=reset()['cameraYaw'];down('LeftArrow')
        try:until(lambda n:abs((n['cameraYaw']-before+180)%360-180)>20,8)
        finally:up('LeftArrow')
        reset();press('C');assert abs(state()['cameraYaw']-180)<2
        press('C');press('C') # Cycle close → high → back to the chase view.
        return moves
    if name=='locomotion':
        s,w=travel(3);assert 2.8<w['speed']<3.8,w
        reset();s,r=travel(6,True);assert r['speed']>w['speed']*1.4,r
        return {'walkMps':w['speed'],'runMps':r['speed']}
    if name=='jump':
        down('Space')
        try:air=until(lambda n:n['jumps']==start['jumps']+1 and n['position'][1]>start['position'][1]+.2 and not n['grounded'])
        finally:up('Space')
        landed=until(lambda n:n['grounded'])
        assert abs(landed['position'][1]-start['position'][1])<.15,landed
        return {'riseMetres':air['position'][1]-start['position'][1]}
    if name=='objective':
        press('Tab');down('UpArrow')
        try:end=until(lambda n:n['reachedGoal'],12)
        finally:up('UpArrow');press('Tab')
        assert end['recoveries']==start['recoveries'],end
        return {'position':end['position']}
    if name=='collision':
        fixture=call('gameplay_collision_fixture');down('UpArrow')
        try:wall=until(lambda n:n['contacts']>fixture['contacts'])
        finally:up('UpArrow')
        call('gameplay_camera','--yaw',fixture['cameraYaw']+180)
        camera=until(lambda n:n['cameraBlocked'] and n['cameraDistance']<2)
        return {'sourceId':wall['wallSource'],'cameraDistance':camera['cameraDistance']}
    if name=='recovery':return verify_recovery()
    if name=='visual':
        until(lambda n:n['frame']>start['frame']+15 and n['grounded'] and n['speed']<.1)
        grounding=call('gameplay_grounding')
        assert grounding['hit'] and abs(grounding['soleGap'])<.1,grounding
        return {'screenshot':capture('padova-'+('editor' if EDITOR else 'native')+'-current.png'),**grounding}
    raise ValueError(name)

def main(args):
    global EDITOR
    EDITOR=args.editor
    (ROOT/'.context').mkdir(parents=True,exist_ok=True)
    headless=getattr(args,'headless',False)
    report={'mode':'editor' if EDITOR else 'native-headless' if headless else 'native','checks':[]}
    stamp=ROOT/'.context/build/playable-source-sha256.txt'
    if not EDITOR:close_previous_player()
    if args.build:
        pipeline.command('build','--target','StandaloneOSX','--outputPath',str(APP),'--options','["Development","AllowDebugging"]','--scenes','["Assets/Scenes/PadovaPlayable.unity"]','--confirm','true',runtime=False)
        deadline=time.monotonic()+600
        while time.monotonic()<deadline:
            b=pipeline.command('build_status',runtime=False)
            if b.get('status')=='completed':
                assert b['result']=='Succeeded',b
                report['build']={k:b[k] for k in ['result','totalErrors','totalWarnings','buildTimeMs']};break
            time.sleep(1)
        else:raise TimeoutError('Build unfinished')
        stamp.write_text(fingerprint()+'\n')
    if not EDITOR:
        if not stamp.exists() or stamp.read_text().strip()!=fingerprint():
            raise RuntimeError('Native build does not match current source/assets. Run with --build.')
        report['sourceSha256']=stamp.read_text().strip()
    player=None
    if EDITOR:
        status=pipeline.command('editor_status',runtime=False)
        if status.get('playMode')!='playing':pipeline.command('editor_play',runtime=False)
    else:
        # --headless: -batchmode opens no window and takes no focus; the game still renders through Metal on request.
        player=subprocess.Popen([str(APP/'Contents/MacOS/padova-open-world'),*(['-batchmode'] if headless else []),'-screen-fullscreen','0','-screen-width','1600','-screen-height','1000','-logFile',str(ROOT/'.context/playable-player.log')],cwd=ROOT,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
    begun=time.monotonic()
    try:
        deadline=time.monotonic()+45
        while True:
            try:
                if player and descriptor().get('pid')!=player.pid:
                    if player.poll() is not None:raise RuntimeError('New Player exited before exposing its runtime')
                    if time.monotonic()>deadline:raise TimeoutError('Runtime descriptor does not belong to the new Player')
                    time.sleep(.1);continue
                s=state()
                if s['ready'] and s['playing'] and s['grounded']:break
            except (RuntimeError,json.JSONDecodeError):pass
            if time.monotonic()>deadline:raise TimeoutError('Scene not ready')
            time.sleep(.2)
        assert s['scene']=='PadovaPlayable',s
        if player:
            report.update(playerPid=player.pid,buildGuid=descriptor().get('buildGuid'))
            if getattr(args,'ignore_focus',False):
                call('gameplay_focus_policy','--ignore','true');report['focusPolicy']='IgnoreFocus (QA only, in memory)'
            if not headless:subprocess.run(['open','-a',str(APP)],check=True)
            focus_end=time.monotonic()+5
            while not state().get('focused',False):
                if time.monotonic()>focus_end:raise RuntimeError('Could not activate the native game window')
                time.sleep(.1)
        for name in args.case.split(','):
            t=time.monotonic();detail=run_case(name)
            result={'name':name,'passed':True,'seconds':round(time.monotonic()-t,2),**detail}
            report['checks'].append(result);print(json.dumps(result),flush=True)
        if args.measure:
            report['performanceSamples']=[]
            for _ in range(4):
                time.sleep(2);report['performanceSamples'].append(call('runtime_status'))
        report['passed']=True
    except Exception as e:
        report['passed']=False;report['error']=repr(e)
        try:report['failureScreenshot']=capture('padova-failure.png')
        except Exception:pass
        raise
    finally:
        report['elapsedSeconds']=round(time.monotonic()-begun,2)
        (ROOT/'.context'/('playable-editor-verification.json' if EDITOR else 'playable-verification.json')).write_text(json.dumps(report,indent=2)+'\n')
        if not EDITOR and player:
            try:
                if descriptor().get('pid')==player.pid:call('quit')
                else:player.terminate()
            except Exception:player.terminate()
            try:player.wait(timeout=10)
            except subprocess.TimeoutExpired:player.kill();player.wait()
    print('PASS',report['elapsedSeconds'],'seconds',flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--build',action='store_true');p.add_argument('--editor',action='store_true')
    p.add_argument('--measure',action='store_true')
    p.add_argument('--headless',action='store_true',help='Run the native Player with -batchmode: no window, no focus stealing; screenshots exclude the HUD')
    p.add_argument('--case',default='animation,controls,locomotion,jump,objective,collision,recovery,visual')
    args=p.parse_args()
    if args.editor and args.build:p.error('--editor and --build are separate gates')
    main(args)
