"""Generate replaceable first-build artwork and an XLSX sample (Pillow required)."""
from PIL import Image, ImageDraw, ImageFont
from pathlib import Path
import math, zipfile
from xml.sax.saxutils import escape
root = Path(__file__).resolve().parents[1] / 'src' / 'Murbingo.App'
assets = root / 'assets'
assets.mkdir(exist_ok=True)
font_path = 'C:/Windows/Fonts/georgia.ttf'
def font(size): return ImageFont.truetype(font_path, size)
im = Image.new('RGB', (1000, 700), '#543b29'); d = ImageDraw.Draw(im)
for y in range(700):
    t = int(12 * math.sin(y * .09) + 6 * math.sin(y * .45))
    d.line((0,y,1000,y), fill=(84+t,59+t//2,41+t//3))
for x in [22,978]: d.line((x,0,x,700),fill='#9a774c',width=2)
if not (assets/'cabinet.png').exists(): im.save(assets/'cabinet.png')
im = Image.new('RGB', (1800, 150), '#183337'); d = ImageDraw.Draw(im)
for y in [8, 13, 137, 142]: d.line((0,y,1800,y),fill='#b39157',width=2)
for x in range(0,1800,24):
    h=18 if x%120==0 else 8
    d.line((x,122,x,122-h),fill='#8c9a87',width=2)
d.text((80,24),'МУРСКОЕ БИНГО 2',font=font(52),fill='#f3dcaf')
d.text((830,40),'ГАМЕЛАН  /  ON AIR  /  1948',font=font(31),fill='#c7aa78')
d.ellipse((1550,48,1570,68),fill='#d57950'); d.text((1590,38),'BINGO',font=font(29),fill='#e9d7b1')
if not (assets/'banner.png').exists(): im.save(assets/'banner.png')
# Friendly radio-host raptor; transparent silhouettes in two distinct poses.
for pose in (1,2):
    if (assets/f'raptor-{pose}.png').exists():
        continue  # Preserve supplied/replaced artwork.
    im = Image.new('RGBA',(360,430)); d=ImageDraw.Draw(im)
    d.polygon([(175,305),(70,350),(18,329),(100,385),(250,379),(290,325)],fill='#52776b')
    d.ellipse((120,226,288,417),fill='#789b80',outline='#293f39',width=5)
    d.ellipse((165,267,260,402),fill='#c6c299')
    d.polygon([(205,190),(245,208),(240,288),(166,279),(153,204)],fill='#789b80',outline='#293f39')
    d.rounded_rectangle((37,70 if pose==1 else 60,300,224),radius=56,fill='#789b80',outline='#293f39',width=5)
    d.ellipse((87,100,142,158),fill='#eee3b9',outline='#293f39',width=3)
    d.ellipse((103 if pose==1 else 110,111,124 if pose==1 else 131,143),fill='#263d35')
    d.ellipse((111,112,117,120),fill='white')
    d.arc((42,145,260,218),0,165,fill='#293f39',width=6)
    d.polygon([(89,204),(101,214),(111,201)], fill='#fff3cf')
    for x in [47,63]: d.ellipse((x,143,x+5,148),fill='#293f39')
    # Headphones, microphone, waistcoat.
    d.arc((116,40,314,221),188,353,fill='#c7a363',width=16)
    d.rounded_rectangle((254,117,310,198),radius=18,fill='#283c38',outline='#c7a363',width=6)
    d.line((279,192,240,232,180,232),fill='#c7a363',width=6)
    d.ellipse((162,222,188,241),fill='#293f39')
    d.polygon([(135,287),(187,321),(154,397),(122,378)],fill='#324d49')
    d.polygon([(266,287),(207,321),(242,397),(283,378)],fill='#324d49')
    if pose==1:
        d.line((150,306,100,275,68,288),fill='#293f39',width=25); d.line((150,306,100,275,68,288),fill='#789b80',width=18)
    else:
        d.line((150,306,101,246,93,211),fill='#293f39',width=25); d.line((150,306,101,246,93,211),fill='#789b80',width=18)
        d.line((94,213,73,195),fill='#789b80',width=10)
    d.ellipse((143,393,192,423),fill='#52776b',outline='#293f39',width=3)
    d.ellipse((224,393,284,423),fill='#52776b',outline='#293f39',width=3)
    im.save(assets/f'raptor-{pose}.png')
rows = [line.split(',') for line in (root/'examples/tickets.csv').read_text(encoding='utf-8').splitlines()]
xml='<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>'
for i,row in enumerate(rows,1):
    xml+=f'<row r="{i}">'
    for j,value in enumerate(row):
        ref=f'{chr(65+j)}{i}'
        xml+=f'<c r="{ref}" t="inlineStr"><is><t>{escape(value)}</t></is></c>' if j==0 else f'<c r="{ref}"><v>{value}</v></c>'
    xml+='</row>'
xml+='</sheetData></worksheet>'
with zipfile.ZipFile(root/'examples/tickets.xlsx','w',zipfile.ZIP_DEFLATED) as z:
    z.writestr('[Content_Types].xml','<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>')
    z.writestr('_rels/.rels','<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>')
    z.writestr('xl/workbook.xml','<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Tickets" sheetId="1" r:id="rId1"/></sheets></workbook>')
    z.writestr('xl/_rels/workbook.xml.rels','<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>')
    z.writestr('xl/worksheets/sheet1.xml',xml)
print('Generated PNG assets and sample XLSX.')
