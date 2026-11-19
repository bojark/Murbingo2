"""Encode the approved PNG app icon into a multi-resolution Windows ICO.

Only format/size conversion; the reference raptor sprites are never modified.
Requires Pillow. The final app.ico is committed with the application assets.
"""
from pathlib import Path
from PIL import Image

assets = Path(__file__).resolve().parents[1] / 'src' / 'Murbingo.App' / 'assets'
image = Image.open(assets / 'app-icon.png').convert('RGBA')
image.save(assets / 'app.ico', format='ICO', sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)])
with Image.open(assets / 'app.ico') as icon:
    sizes = icon.info['sizes']
    assert {(16, 16), (32, 32), (48, 48), (256, 256)}.issubset(sizes)
print('Created app.ico with', len(sizes), 'resolutions.')
