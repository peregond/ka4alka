"""Capture only the published app's composited window; its binary stays untouched."""
import collections,json,os,subprocess,sys,tempfile,time
from pathlib import Path

app,output,helper=sys.argv[1:]
root=Path(output);root.mkdir(parents=True,exist_ok=True)
composited=root/'composited';composited.mkdir(exist_ok=True)
frames=collections.deque(maxlen=20);saved={};window=None
deadline=time.monotonic()+900

def preserve():
    for source in root.glob('*.png'):
        if source.name in saved:continue
        timestamp=source.stat().st_mtime
        earlier=[frame for frame in frames if frame[0]<=timestamp-0.20]
        if not earlier:continue
        stamp,pixels=earlier[-1]
        if timestamp-stamp>2:raise RuntimeError('No recent composited frame for '+source.name)
        (composited/source.name).write_bytes(pixels)
        saved[source.name]={'SourceTimestamp':timestamp,'CompositedTimestamp':stamp,'WindowId':window}

with (root/'app.log').open('wb') as log, tempfile.TemporaryDirectory(prefix='ka4alka-composited-') as scratch:
    process=subprocess.Popen([app,'--smoke-test',str(root),'-ApplePersistenceIgnoreState','YES'],stdout=log,stderr=subprocess.STDOUT)
    try:
        while process.poll() is None:
            if time.monotonic()>deadline:raise TimeoutError('Published Mac capture timed out')
            if window is None:
                found=subprocess.run([helper,str(process.pid)],capture_output=True,text=True)
                if found.returncode==0:window=int(found.stdout.strip())
                else:time.sleep(0.1);continue
            target=Path(scratch)/'frame.png';start=time.time()
            capture=subprocess.run(['/usr/sbin/screencapture','-x','-o','-l',str(window),'-t','png',str(target)],capture_output=True,timeout=10)
            if capture.returncode==0 and target.is_file():
                frames.append(((start+time.time())/2,target.read_bytes()))
            preserve()
            time.sleep(0.05)
        preserve()
        if process.returncode:raise RuntimeError('Published Mac application failed; see app.log')
        missing=[p.name for p in root.glob('*.png') if p.name not in saved]
        if missing:raise RuntimeError('Missing composited application screenshots: '+', '.join(missing))
        if not saved:raise RuntimeError('No native application screenshots were captured')
        (composited/'capture-times.json').write_text(json.dumps(saved,indent=2)+'\n')
        print('Captured actual composited Mac window frames:',len(saved),flush=True)
    finally:
        if process.poll() is None:process.terminate();process.wait(timeout=15)
