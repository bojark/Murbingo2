"""Render the exact requested test title as a transparent PNG using local fonts."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
root = Path(__file__).resolve().parents[1]
image = Image.new('RGBA', (1600,160), (0,0,0,0))
draw = ImageDraw.Draw(image)
font = ImageFont.truetype('C:/Windows/Fonts/georgiab.ttf',100)
title = 'Мурское Бинго'
box = draw.textbbox((0,0),title,font=font,stroke_width=3)
x = (image.width-(box[2]-box[0]))/2-box[0]
y = (image.height-(box[3]-box[1]))/2-box[1]
draw.text((x,y),title,font=font,fill='#3C2417',stroke_width=3,stroke_fill='#FFF2C8')
image.save(root/'src/Murbingo.App/assets/banner-title.png',optimize=True)
print('Saved transparent banner-title.png')
