"""Ink, paper keycaps, and the four articulated paws of Mo Zhua.

Artwork is drawn at four times the target resolution. Paths use absolute M,
L, Q, C, Z commands, in a 192 by 208 design grid.
"""

import math
from functools import lru_cache

import aggdraw
from PIL import Image, ImageDraw, ImageFont

from desk import BOARD_BACK, CAT_SCALE, DECK, KEYS, cat_point, key_center, project

INK = "#171822"
COAT = "#262631"
MID = "#30303C"
LIGHT = "#50505D"
RIM = "#EEE4CD"
SOCK = "#FFF1D8"
SHADE = "#D7BFA7"
GOLD = "#FFBD37"
SCALE = 4
CAP_LAUNCH = .18
CAP_LANDING = .97


class Brush:
    def __init__(self, image, transform=None):
        self.image = image
        self.transform = transform or (lambda x, y: (x, y))

    def point(self, x, y):
        x, y = self.transform(x, y)
        return x * SCALE, y * SCALE

    def path(self, data, fill=None, line=None, width=1):
        parts = iter(data.split())
        path = aggdraw.Path()

        def point():
            return self.point(float(next(parts)), float(next(parts)))

        for command in parts:
            if command == "M":
                last = point()
                path.moveto(*last)
            elif command == "L":
                last = point()
                path.lineto(*last)
            elif command == "C":
                a, b, last = point(), point(), point()
                path.curveto(*a, *b, *last)
            elif command == "Q":
                control, end = point(), point()
                a = tuple((v + 2*c)/3 for v, c in zip(last, control))
                b = tuple((v + 2*c)/3 for v, c in zip(end, control))
                path.curveto(*a, *b, *end)
                last = end
            elif command == "Z":
                path.close()
            else:
                raise ValueError(command)
        draw = aggdraw.Draw(self.image)
        draw.path(path, aggdraw.Brush(fill) if fill else None,
                  aggdraw.Pen(line, width * SCALE) if line else None)
        draw.flush()

    def oval(self, x, y, rx, ry, fill, line=None, width=1):
        k = .552285
        self.path(f"M {x-rx} {y} C {x-rx} {y-k*ry} {x-k*rx} {y-ry} {x} {y-ry} "
                  f"C {x+k*rx} {y-ry} {x+rx} {y-k*ry} {x+rx} {y} "
                  f"C {x+rx} {y+k*ry} {x+k*rx} {y+ry} {x} {y+ry} "
                  f"C {x-k*rx} {y+ry} {x-rx} {y+k*ry} {x-rx} {y} Z", fill, line, width)


# Key legends are drawn at supersampled resolution, then rotated into the
# typist's view. Pillow's bundled typeface makes regeneration portable.
@lru_cache(maxsize=192)
def legend_stamp(text, width, height):
    font = ImageFont.load_default(size=32)
    l,t,r,b = font.getbbox(text)
    stamp = Image.new("RGBA", (max(1,r-l+2), max(1,b-t+2)))
    ImageDraw.Draw(stamp).text((1-l,1-t),text,font=font,fill="#564D46",
                               stroke_width=0)
    factor = min(width/stamp.width, height/stamp.height)
    return stamp.resize((max(1,round(stamp.width*factor)),max(1,round(stamp.height*factor))),
                        Image.Resampling.LANCZOS).transpose(Image.Transpose.ROTATE_180)


def return_legend(b, x, y):
    b.path(f"M {x-2.9} {y+1.05} L {x-2.9} {y-.9} L {x+2.1} {y-.9} "
           f"M {x+.65} {y-1.9} L {x+2.2} {y-.9} L {x+.85} {y+.2}",
           None, "#8F6439", .75)


def panel_quad(b, points, fill):
    a,c,d,e = points
    b.path(f"M {a[0]} {a[1]} L {c[0]} {c[1]} L {d[0]} {d[1]} L {e[0]} {e[1]} Z",fill)


@lru_cache(maxsize=10)
def keyboard(pressed, missing_return):
    # Each switch sits in a recessed top plate; the fascia still faces the
    # viewer while the legends, clusters and bevels follow the cat's key layout.
    img = Image.new("RGBA",(192*SCALE,208*SCALE))
    b = Brush(img)
    b.path("M 31 160 Q 31.5 157.6 35 157.6 L 177.8 157.6 Q 180.4 157.6 181 160 "
           "L 190 197.7 Q 191.4 202.5 187.5 203.5 L 20 203.5 "
           "Q 15.1 203.4 17.1 197.8 Z", INK,RIM,1.45)
    b.path("M 32.5 160.5 Q 33.1 158.5 35.3 158.5 L 177.2 158.5 Q 179.5 158.5 180 160.8 "
           "L 188.6 197.8 Q 189.4 200.3 186.8 200.4 L 20.9 200.4 Q 18.1 200.4 19.1 197.7 Z","#C4B59B")
    panel_quad(b,[(32.4,159.35),(178.5,159.35),(187.4,199.1),(19.5,199.1)],"#93877A")
    mods = {"0F","3A","2A","36","1D","E01D","E05B","E05C","38","E038","E05D","0E"}
    arrows = {"Up":(0,1), "Left":(1,0), "Down":(0,-1), "Right":(-1,0)}
    for key in DECK:
        a, c, d, e = key.cap()
        x,y,w,h = key.target()
        if key.code == "1C" and missing_return:
            panel_quad(b, (a,c,d,e), "#665B52")
            b.oval(x,y,1.55,1.07,"#BF9F76")
            continue
        tone = {"01":"#D99480","1C":"#EDB75D","39":"#F3DDAD",
                "45":"#B0C3B2","E01C":"#DCC499"}.get(key.code)
        if not tone: tone = "#DEDDCF" if key.code in mods else "#F7ECD8"
        drop = .65 if key.code == pressed else 0
        if drop: tone = "#EEAB59"
        panel_quad(b,(a,c,d,e),"#AE9C87")
        panel_quad(b,((a[0]+.09,a[1]+drop),(c[0]-.09,c[1]+drop),
                      (d[0]-.12,d[1]-.87),(e[0]+.12,e[1]-.87)),tone)
        y += (drop-.45)/2
        if key.code == "39":
            b.path(f"M {x-3.5} {y+.7} L {x-3.5} {y-.4} L {x+3.5} {y-.4} L {x+3.5} {y+.7}",
                   None,"#A98D61",.44)
        elif key.legend in arrows:
            dx,dy = arrows[key.legend]
            tip = (x+dx*1.6,y+dy*1.6)
            b.path(f"M {x-dx*1.5} {y-dy*1.5} L {tip[0]} {tip[1]} "
                   f"M {x+dy} {y-dx} L {tip[0]} {tip[1]} L {x-dy} {y+dx}",
                   None,"#5E5349",.56)
        else:
            stamp = legend_stamp(key.legend,round((w-.8)*SCALE),
                                 round(min(h-1.0,3.6)*SCALE))
            img.alpha_composite(stamp,(round(x*SCALE-stamp.width/2), round(y*SCALE-stamp.height/2)))
    # Three lock indicators in the well above the number pad.
    for offset,paint in enumerate(("#D9AD62","#8EAC9A","#69665F")):
        x,y = project(19.3 + offset*.8,.47)
        b.oval(x,y,.62,.47,paint)
    b.path("M 23 201.8 L 72 201.8", None, "#675B59", .6)
    return img


def mouse(b):
    # A contrasting cream mouse, with a dark rubber underside and amber wheel.
    b.path("M 17 167 C 22 163 30 166 31 173 C 33 181 28 188 21 189 "
           "C 13 190 10 184 12 177 Q 13 170 17 167 Z", INK, RIM, 2.3)
    b.path("M 17 169 C 22 165 28 168 29 173 C 30 180 26 185 21 186 "
           "C 15 187 13 183 14 178 Q 15 172 17 169 Z", SOCK)
    b.path("M 24 167.5 Q 23 171 22 176", None, SHADE, 1)
    b.oval(22.4, 172.3, 1.05, 2.45, "#C58A42")
    b.path("M 14 178.1 L 13.7 180.7 M 13.7 174.9 L 13.6 176.9", None, "#9A826B", .8)


def tail(b, p):
    x, y, wag = p.x, p.y, p.tail
    b.path(f"M {121+x} {142+y} C 143 {163+wag*.2} 148 {140+wag} 153 {130+wag} "
           f"C 158 {120+wag} 164 {117+wag} 173 {118+wag} "
           f"C 183 {119+wag} 181 {131+wag} 172 {132+wag} "
           f"C 157 {132+wag} 161 {150+wag*.3} 148 {157+wag*.25} "
           f"Q 135 166 {123+x} {159+y} Z", INK, RIM, 2.2)
    b.path(f"M 152 {138+wag*.7} Q 158 {119+wag} 172 {121+wag}", None, LIGHT, 1.5)


def body(b):
    b.path("M 66 89 Q 60 103 58 118 Q 50 128 52 145 C 52 161 70 169 94 167 "
           "C 121 171 139 159 138 140 Q 139 125 131 114 Q 127 102 123 91 Z",
           COAT, RIM, 2.2)
    b.path("M 65 103 C 58 126 63 154 80 160 C 98 168 124 162 133 148 "
           "C 130 170 94 172 71 163 C 47 154 51 135 58 122 Z", "#20212B")
    b.path("M 70 109 Q 78 116 86 115 L 83 112 Q 89 116 95 116 "
           "L 92 113 Q 96 115 100 114", None, "#43434D", 1.25)


def limb(b, start, end, rear=False):
    sx, sy = start
    x, y = end
    w = 7.4 if rear else 6.2
    vx, vy = x - sx, y - sy
    length = max(math.hypot(vx, vy), 1)
    nx, ny = -vy / length * w, vx / length * w
    # Rounded, slightly bowed arm, narrowing into the cuff.
    bend = 3 if sx < 95 else -3
    b.path(f"M {sx+nx} {sy+ny} Q {sx+vx*.4+nx+bend} {sy+vy*.44+ny} {x+nx*.8} {y+ny*.8} "
           f"Q {x+vx/length*5} {y+vy/length*5} {x-nx*.8} {y-ny*.8} "
           f"Q {sx+vx*.4-nx+bend} {sy+vy*.44-ny} {sx-nx} {sy-ny} "
           f"Q {sx-vx/length*7} {sy-vy/length*7} {sx+nx} {sy+ny} Z",
           "#252630" if rear else MID, "#1C1D28", 1.3)
    pr = 7 if rear else 6.4
    b.path(f"M {x-pr} {y-2} Q {x-pr} {y-4.4} {x-pr+1.9} {y-4.9} "
           f"Q {x} {y-3.2} {x+pr-1} {y-5.3} Q {x+pr+2.8} {y} {x+pr-.3} {y+3.5} "
           f"C {x+pr-2.2} {y+7.1} {x-pr+1.8} {y+7.5} {x-pr} {y+3.2} Z",
           SOCK, "#4B4448", 1.15)
    for offset in (-2, 1.4):
        b.path(f"M {x+offset} {y+4.8} Q {x+offset+1.2} {y+4} {x+offset+.8} {y+2.5}",
               None, "#BEA794", .8)


def head(b, p):
    shape = ("M 55 58 C 55 49 54 39 56 30 Q 56.8 24 61 26 "
             "Q 68 29 78 40 Q 91 36 105 39 Q 109 38 113 38 "
             "Q 124 25 131 23 Q 135 22 136 29 L 138 46 L 136 51 "
             "Q 142 64 140 78 L 143 82 L 139 83 Q 135 99 118 105 "
             "Q 101 112 79 107 Q 60 104 54 90 L 50 88 L 52 85 "
             "Q 48 71 55 58 Z")
    b.path(shape, COAT, RIM, 2.3)
    b.path("M 58 51 Q 57 39 59 31 Q 65 33 72 43 Q 64 45 58 51 Z", "#66545A")
    b.path("M 117 40 Q 126 29 131 28 Q 133 34 133 45 Q 126 41 117 40 Z", "#66545A")
    b.path("M 60 46 Q 60 38 61 36 L 67 42 Z", "#99746E")
    b.path("M 123 39 Q 128 33 130 32 L 131 42 Z", "#99746E")
    b.path("M 59 55 Q 65 44 80 43 Q 87 40 98 42", None, "#41414C", 1.45)
    b.path("M 54 83 Q 65 98 84 100 Q 112 108 135 91 Q 127 106 105 108 "
           "Q 69 113 54 90 Z", "#21222D")
    # Short hatch strokes and asymmetric ear details keep the silhouette legible.
    for data in ("M 62 53 Q 65 50 68 49", "M 68 51 L 71 49",
                 "M 128 49 L 131 51"):
        b.path(data, None, LIGHT, .9)
    for side in (-1, 1):
        whisker = 59 if side == -1 else 133
        for i, (rise, span) in enumerate([(-1, 12), (2.6, 14), (5, 10)]):
            y = 84 + i * 2.7
            b.path(f"M {whisker} {y} Q {whisker+side*6} {y+.4} "
                   f"{whisker+side*span} {y+rise}", None, "#BCB2A9", .95)
    nx, ny = 97 + 2.2 * p.look_x, 84 + 1.5 * p.look_y
    b.path(f"M {nx-2.4} {ny-.7} Q {nx} {ny-1.7} {nx+2.2} {ny-.8} "
           f"Q {nx+.2} {ny+3.2} {nx-2.4} {ny-.7} Z", "#B79991")
    if p.jaw > .2:
        b.oval(nx, ny+5, 2+p.jaw, 1+2.8*p.jaw, "#14141B")
        b.oval(nx+.3, ny+5.7, 1.55, .7+p.jaw*.7, "#CA8882")
    else:
        b.path(f"M {nx} {ny+1.2} Q {nx-1} {ny+4.3} {nx-3} {ny+3.5} "
               f"M {nx} {ny+1.2} Q {nx+1} {ny+4.4} {nx+3.4} {ny+3.1}", None, "#70626B", .9)


def eye(b, x, y, rx, ry, p):
    h = ry * p.open
    if h < 2:
        b.path(f"M {x-rx} {y} Q {x} {y+3.8} {x+rx} {y-.4}", None, GOLD, 2)
        return
    b.oval(x, y, rx, h, GOLD, INK, 1.05)
    b.path(f"M {x-rx*.78} {y+h*.36} Q {x-rx*.28} {y+h*1.12} "
           f"{x+rx*.52} {y+h*.68}", None, "#FFDB73", 1.35)
    px, py = x + p.look_x * (rx*.50), y + p.look_y * (h*.39)
    b.oval(px, py, rx*.30, h*.55, "#1A1823")
    b.oval(px-1, py-h*.25, 1.05, min(1.2, h*.23), "#FFF2C6")


def flying_keycap(image, t, head_point):
    """The displaced return cap tumbles, perches between the ears, then falls."""
    if not CAP_LAUNCH < t < CAP_LANDING:
        return
    hat_x, hat_y = head_point(105, 37)
    key_x, key_y = key_center("enter")
    if t < .4:
        progress = (t-CAP_LAUNCH)/(.4-CAP_LAUNCH)
        x = key_x + (hat_x-key_x)*progress - 18*math.sin(math.pi*progress)
        y = key_y + (hat_y-key_y)*progress - 35*math.sin(math.pi*progress)
        tilt = 68*math.sin(math.pi*progress) - 13*progress
    elif t < .66:
        x, y, tilt = hat_x, hat_y, -13
    else:
        progress = (t-.66)/(CAP_LANDING-.66)
        x = hat_x + (key_x-hat_x)*progress - 11*math.sin(math.pi*progress)
        y = hat_y + (key_y-hat_y)*progress*progress
        tilt = -13 - 347*progress
    angle = math.radians(tilt)
    co, si = math.cos(angle), math.sin(angle)
    cap = Brush(image, lambda u,v: (x+u*co-v*si, y+u*si+v*co))
    cap.path("M -8 -4 Q -8 -5 -6 -5 L 5 -5 Q 6 -5 7 -4 L 10 2 "
             "Q 11 4 9 5 L -8 5 Q -10 5 -10 3 Z", "#DAA760", INK, 1.2)
    cap.path("M -6 -4 L 5 -4 L 7 0 Q 7 1 6 1 L -7 1 Z", "#FFF0C7")
    return_legend(cap, -.5, -1.4)
    cap.path("M -7 2 L 7 2", None, "#B08451", .9)
    if .08 < t < .36 or .73 < t < .94:
        cap.path("M -3 8 L -2 14 M 2 8 L 3 11", None, "#A48B64", 1.2)


def joints(p):
    return {
        "RL": ((67+p.x, 142+p.y), p.rear_l, True),
        "RR": ((125+p.x,142+p.y), p.rear_r, True),
        "FL": ((73+p.x,116+p.y), p.front_l, False),
        "FR": ((119+p.x,115+p.y), p.front_r, False),
    }


def render(p, eyes=True, size=(192, 208), live_base=False):
    image = Image.new("RGBA", (192*SCALE, 208*SCALE))
    b = Brush(image)
    # Ground the hind paws on the far side of the keyboard. The shadow stays
    # on the ground during jumps, across the strip of air behind the back rail.
    floor = ImageDraw.Draw(image)
    floor.ellipse(tuple(int(v*SCALE) for v in (59, 148.6, 139, 155)), fill=(48, 40, 42, 50))
    cat = Brush(image, cat_point)
    tail(cat, p)
    torso = Brush(image, lambda x,y: cat_point(x + p.x, y + p.y))
    body(torso)
    limbs = joints(p)
    reaching = []
    def paint_limb(start, end, rear):
        if live_base:
            return
        if cat_point(*end)[1] + 6 >= BOARD_BACK:
            reaching.append((start, end, rear))
        else:
            limb(cat, start, end, rear)
    for name in ("RL","RR"):
        paint_limb(*limbs[name])
    angle = math.radians(p.tilt)
    co, si = math.cos(angle), math.sin(angle)

    def head_point(x, y):
        u, v = x - 96, y - 85
        return cat_point(96 + u*co - v*si + p.x + p.head_x,
                85 + u*si + v*co + p.y + p.head_y)

    hb = Brush(image, head_point)
    head(hb, p)
    geometry = []
    for x, y, rx, ry in ((75, 70, 13.7, 13.2), (115.2, 68.6, 14.6, 13.7)):
        x, y = head_point(x + p.look_x*1.3, y + p.look_y*1.1)
        rx, ry = rx * CAT_SCALE, ry * CAT_SCALE
        geometry.append((x, y, rx, ry))
        if eyes:
            eye(b, x, y, rx, ry, p)
    for name in ("FL","FR"):
        paint_limb(*limbs[name])
    pressed = KEYS.get(p.key) if not live_base and p.press > .65 else None
    image.alpha_composite(keyboard(pressed, CAP_LAUNCH < p.keycap_time < CAP_LANDING))
    mouse(Brush(image, lambda x,y: (35.9-x, 356.8-y)))
    for start, end, rear in reaching:
        limb(cat, start, end, rear)
    flying_keycap(image, p.keycap_time, head_point)
    return image.resize(size, Image.Resampling.LANCZOS), geometry
