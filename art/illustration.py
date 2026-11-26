"""Ink, paper keycaps, and the four articulated paws of Mo Zhua.

Artwork is drawn at four times the target resolution. Paths use absolute M,
L, Q, C, Z commands, in a 192 by 208 design grid.
"""

import math

import aggdraw
from PIL import Image, ImageDraw

from desk import BOARD_BACK, CAT_SCALE, KEYS, cat_point, key_center, keys

INK = "#171822"
COAT = "#262631"
MID = "#30303C"
LIGHT = "#50505D"
RIM = "#EEE4CD"
SOCK = "#FFF1D8"
SHADE = "#D7BFA7"
GOLD = "#FFBD37"
SCALE = 4


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


def keyboard(b, p):
    b.path("M 39 162 Q 40 159.5 44 159.5 L 169 159.5 Q 172 159.5 173 162 "
           "L 186.5 192 Q 190.5 201 183.5 202 L 27 202 Q 21.5 202 25 193 Z",
           INK, RIM, 2)
    b.path("M 40 163 Q 40.8 161 44 161 L 168 161 Q 170.5 161 171.5 163 "
           "L 184.8 193.7 Q 186.5 197.2 183 197.5 L 28.5 197.5 Q 26 197.5 27.2 194 Z",
           "#B7A183")
    b.path("M 43 162.5 L 169 162.5 L 182.3 195.2 L 29.6 195.2 Z", "#A49380")
    accents = {(0, 0): "#EA9271", (2, 9): "#F2BA52", (3, 3): "#F4DCAB", (0, 11): "#96B7AA"}
    for (row, col), (left, top, right, height) in keys():
        bottom = top + height
        if (row, col) == KEYS["enter"] and .04 < p.keycap_time < .97:
            b.path(f"M {left} {top} L {right} {top} L {right+.6} {bottom} L {left-.7} {bottom} Z",
                   "#655D56")
            b.oval((left+right)/2, (top+bottom)/2, 2.2, 1.45, "#BE976E")
            continue
        tone = accents.get((row, col), "#F4E8D3" if (col + row) % 3 else "#FFF3DD")
        if (row, col) == KEYS.get(p.key) and p.press > .65:
            top += 1.3
            tone = "#F6AC56"
        # Show each cap's front wall below the lighter typing surface.
        b.path(f"M {left+.4} {top} L {right-.4} {top} L {right+.35} {bottom-1} "
               f"Q {right+.45} {bottom} {right-.5} {bottom} L {left-.6} {bottom} Z", "#C6B09A")
        b.path(f"M {left+.6} {top} L {right-.6} {top} L {right} {bottom-1.6} "
               f"L {left-.4} {bottom-1.6} Z", tone)
        if (row, col) == KEYS["enter"]:
            b.path(f"M {right-4} {top+1} L {right-4} {top+2.7} L {right-7} {top+2.7}",
                   None, "#966D3D", .8)
        elif (row, col) != KEYS["space"]:
            b.path(f"M {left+2.2} {top+1.35} L {left+3.9} {top+1.35}", None, "#9B8A77", .7)
    b.path("M 31 199.6 L 89 199.6", None, "#5C5050", .85)
    b.oval(177.5, 199.5, 1, .6, "#C8A060")


def mouse(b):
    # A contrasting cream mouse, with a dark rubber underside and amber wheel.
    b.path("M 17 167 C 22 163 30 166 31 173 C 33 181 28 188 21 189 "
           "C 13 190 10 184 12 177 Q 13 170 17 167 Z", INK, RIM, 2.3)
    b.path("M 17 169 C 22 165 28 168 29 173 C 30 180 26 185 21 186 "
           "C 15 187 13 183 14 178 Q 15 172 17 169 Z", SOCK)
    b.path("M 24 167.5 Q 23 171 22 176", None, SHADE, 1)
    b.oval(22.4, 172.3, 1.05, 2.45, "#C58A42")


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
    if not .04 < t < .97:
        return
    hat_x, hat_y = head_point(105, 37)
    key_x, key_y = key_center("enter")
    if t < .4:
        progress = (t-.04)/.36
        x = key_x + (hat_x-key_x)*progress + 18*math.sin(math.pi*progress)
        y = key_y + (hat_y-key_y)*progress - 35*math.sin(math.pi*progress)
        tilt = 68*math.sin(math.pi*progress) - 13*progress
    elif t < .66:
        x, y, tilt = hat_x, hat_y, -13
    else:
        progress = (t-.66)/.31
        x = hat_x + (key_x-hat_x)*progress + 11*math.sin(math.pi*progress)
        y = hat_y + (key_y-hat_y)*progress*progress
        tilt = -13 - 125*progress
    angle = math.radians(tilt)
    co, si = math.cos(angle), math.sin(angle)
    cap = Brush(image, lambda u,v: (x+u*co-v*si, y+u*si+v*co))
    cap.path("M -8 -4 Q -8 -5 -6 -5 L 5 -5 Q 6 -5 7 -4 L 10 2 "
             "Q 11 4 9 5 L -8 5 Q -10 5 -10 3 Z", "#DAA760", INK, 1.2)
    cap.path("M -6 -4 L 5 -4 L 7 0 Q 7 1 6 1 L -7 1 Z", "#FFF0C7")
    cap.path("M -7 2 L 7 2", None, "#B08451", .9)
    if .08 < t < .36 or .73 < t < .94:
        cap.path("M -3 8 L -2 14 M 2 8 L 3 11", None, "#A48B64", 1.2)


def render(p, eyes=True, size=(192, 208)):
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
    legs = [((67+p.x, 142+p.y), p.rear_l, True),
            ((125+p.x, 142+p.y), p.rear_r, True)]
    arms = [((73+p.x, 116+p.y), p.front_l, False),
            ((119+p.x, 115+p.y), p.front_r, False)]
    reaching = []
    def paint_limb(start, end, rear):
        if cat_point(*end)[1] + 6 >= BOARD_BACK:
            reaching.append((start, end, rear))
        else:
            limb(cat, start, end, rear)
    for leg in legs:
        paint_limb(*leg)
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
    for arm in arms:
        paint_limb(*arm)
    keyboard(b, p)
    mouse(Brush(image, lambda x,y: (x-8, y+3)))
    for start, end, rear in reaching:
        limb(cat, start, end, rear)
    flying_keycap(image, p.keycap_time, head_point)
    return image.resize(size, Image.Resampling.LANCZOS), geometry
