"""Extract transparent dancing-Pepe frames and pack them into one portable PNG atlas.

Requires FFmpeg and Pillow only during preparation; the Windows app needs neither.
The source video is read only and its audio track is deliberately excluded.
"""
from pathlib import Path
from PIL import Image
import argparse
import json
import math
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--ffmpeg', required=True)
parser.add_argument('--video')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
video = Path(args.video) if args.video else root/'tools/source-video/empty-pool-pepe.mp4'
frames_dir = root/'.build/empty-pool-frames'
frames_dir.mkdir(parents=True, exist_ok=True)
for stale in frames_dir.glob('frame-*.png'):
    assert stale.resolve().parent == frames_dir.resolve()
    stale.unlink()
subprocess.run([args.ffmpeg, '-y', '-i', str(video), '-an', '-vf',
    'fps=20,format=rgba,colorkey=0x041FF1:0.26:0.06,scale=512:-1:flags=lanczos',
    str(frames_dir/'frame-%03d.png')], check=True, capture_output=True)
frames = []
boxes = []
for path in sorted(frames_dir.glob('frame-*.png')):
    frame = Image.open(path).convert('RGBA')
    alpha = frame.getchannel('A').point(lambda value: 0 if value <= 16 else value)
    frame.putalpha(alpha)
    boxes.append(alpha.getbbox())
    frames.append(frame)
assert frames and all(boxes)
left = max(0, min(b[0] for b in boxes)-4)
top = max(0, min(b[1] for b in boxes)-4)
right = min(frames[0].width, max(b[2] for b in boxes)+4)
bottom = min(frames[0].height, max(b[3] for b in boxes)+4)
width = math.ceil((right-left)/4)*4
height = math.ceil((bottom-top)/4)*4
columns = 7
atlas = Image.new('RGBA', (columns*width, math.ceil(len(frames)/columns)*height))
for i, frame in enumerate(frames):
    atlas.paste(frame.crop((left,top,right,bottom)), ((i%columns)*width,(i//columns)*height))
assets = root/'src/Murbingo.App/assets'
atlas.save(assets/'empty-pool-pepe.png', optimize=True)
manifest = {'sheet':'empty-pool-pepe.png','frameWidth':width,'frameHeight':height,
    'columns':columns,'frameCount':len(frames),'fps':20}
(assets/'empty-pool-pepe.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
preview = Image.new('RGBA', (width,height), '#171F20')
preview.alpha_composite(atlas.crop((0,0,width,height)))
preview.convert('RGB').save(root/'.build/pepe-preview.png')
print('Prepared transparent silent animation:', manifest)
