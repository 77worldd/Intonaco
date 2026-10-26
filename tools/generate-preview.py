from PIL import Image, ImageDraw, ImageFont
import pathlib
ROOT=pathlib.Path("/home/user/Intonaco")
W,H=1040,1200
BG=(32,32,32); CARD=(45,45,45); BORDER=(61,61,61); T1=(255,255,255); T2=(153,153,153); ACC=(0,120,212); ACC2=(0,90,158)
img=Image.new("RGB",(W,H),BG)
d=ImageDraw.Draw(img,"RGBA")
F="/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"; FB="/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
def font(p,s): return ImageFont.truetype(p,s)
# window title bar dots
for i,c in enumerate([(255,95,86),(255,189,46),(39,201,63)]):
    x=970-i*0  # skip
# title bar
d.rectangle([0,0,W,64],fill=(27,27,27))
icon=Image.open(ROOT/"src/Intonaco/Assets/intonaco.png").resize((36,36),Image.LANCZOS)
img.paste(icon,(24,14),icon)
d.text((76,20),"Intonaco",font=font(F,24),fill=T2)
d.text((940,14),"—   □   ✕",font=font(F,26),fill=T2)
# header
d.text((40,96),"Intonaco",font=font(FB,44),fill=T1)
d.text((40,150),"Live wallpaper for Windows",font=font(F,26),fill=T2)
# preview card
d.rounded_rectangle([40,204,1000,604],radius=16,fill=CARD,outline=BORDER,width=2)
# video gradient inside
vid=Image.new("RGB",(928,368))
vp=vid.load()
for y in range(368):
    for x in range(0,928,4):
        t=(x/928*0.6+y/368*0.4)
        r=int(16+40*t); g=int(42+80*(1-t)); b=int(90+120*t)
        for dx in range(4): vp[x+dx,y]=(r,g,b)
mask=Image.new("L",(928,368),0); md=ImageDraw.Draw(mask); md.rounded_rectangle([0,0,928,368],radius=14,fill=255)
img.paste(vid,(56,220),mask)
# waves
d.line([(56,470),(240,430),(430,490),(620,425),(810,485),(984,440)],fill=(255,255,255,70),width=5)
d.line([(56,510),(240,470),(430,530),(620,465),(810,525),(984,480)],fill=(255,255,255,45),width=5)
# play glyph center
cx,cy=520,404
d.ellipse([cx-52,cy-52,cx+52,cy+52],fill=(0,0,0,90),outline=(255,255,255,160),width=3)
d.polygon([(cx-16,cy-24),(cx-16,cy+24),(cx+28,cy)],fill=(255,255,255,235))
# file row
d.text((40,632),"ocean-waves-4k.mp4",font=font(F,30),fill=T1)
d.text((40,672),"MP4 • 48.2 MB • 0:30",font=font(F,24),fill=T2)
d.rounded_rectangle([760,628,1000,696],radius=8,fill=ACC)
d.text((880,662),"Choose file",font=font(F,28),fill=(255,255,255),anchor="mm")
# controls card
d.rounded_rectangle([40,724,1000,836],radius=16,fill=CARD,outline=BORDER,width=2)
# play/pause button glyphs as shapes
def circle_btn(x,y): d.ellipse([x,y,x+64,y+64],fill=(61,61,61))
circle_btn(72,748); d.rectangle([92,766,102,794],fill=T1); d.rectangle([108,766,118,794],fill=T1)
circle_btn(152,748); d.rectangle([172,766,196,794],fill=T1)
circle_btn(232,748); d.polygon([(250,782),(262,774),(262,790)],fill=T1); d.rectangle([248,778,262,786],fill=T1)
d.line([(330,780),(760,780)],fill=(80,80,80),width=8); d.line([(330,780),(480,780)],fill=ACC,width=8)
d.ellipse([464,764,496,796],fill=ACC)
d.text((980,780),"Playing",font=font(F,26),fill=(68,204,68),anchor="rm")
# settings card
d.rounded_rectangle([40,860,1000,1108],radius=16,fill=CARD,outline=BORDER,width=2)
d.text((72,884),"Settings",font=font(FB,34),fill=T1)
def toggle(x,y,on,label):
    d.rounded_rectangle([x,y,x+84,y+42],radius=21,fill=ACC if on else (61,61,61),outline=(90,90,90),width=2)
    tx=x+48 if on else x+6
    d.ellipse([tx,y+6,tx+30,y+36],fill=(255,255,255))
    d.text((x+104,y+6),label,font=font(F,27),fill=T1)
toggle(72,940,True,"Pause when fullscreen app is active")
toggle(72,1000,False,"Start with Windows")
d.text((72,1062),"Frame rate limit",font=font(F,27),fill=T1)
d.rounded_rectangle([800,1050,968,1094],radius=6,fill=(51,51,51),outline=(90,90,90),width=2)
d.text((820,1072),"30 fps  ⌄",font=font(F,26),fill=T1,anchor="lm")
# footer
d.text((980,1150),"v1.0.0",font=font(F,24),fill=T2,anchor="rm")
d.text((40,1150),"Preview graphic — rendered from the app's UI layout",font=font(F,22),fill=(110,110,110),anchor="lm")
out=ROOT/"docs/screenshot-placeholder.png"; out.parent.mkdir(exist_ok=True)
img.save(out); print("wrote",out, img.size)
