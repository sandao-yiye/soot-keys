"""Perspective deck and contact geometry in the 192 × 208 sprite cell.

The layout is specified in key units from the typist's seat. The camera looks
back at the typist, so both axes reverse. Rendered legends and desktop hit
targets all come from the same physical scan-code entries.
"""

from dataclasses import dataclass

CAT_SCALE = .89
CAT_ORIGIN = (12, 2)
BOARD_BACK = 158
KEYS = {"tapLeft": "1E", "tapRight": "26", "space": "39", "enter": "1C", "mouse": "M3"}
MOUSE_BUTTONS = {
    "M1": (18.4, 183.0, 5.2, 6.5), "M2": (9.2, 182.0, 4.8, 6.3),
    "M3": (13.5, 184.5, 2.0, 4.5), "M4": (22.0, 176.1, 1.4, 2.4),
    "M5": (22.2, 179.4, 1.4, 2.4),
}


def cat_point(x, y):
    return CAT_ORIGIN[0] + x * CAT_SCALE, CAT_ORIGIN[1] + y * CAT_SCALE


def cat_coordinates(screen_point):
    return tuple((v - offset) / CAT_SCALE for v, offset in zip(screen_point, CAT_ORIGIN))


def project(x, y):
    depth = (6.18 - y) / 6.18
    left_edge = 33 - 12.4 * depth
    right_edge = 178 + 8.4 * depth
    return right_edge - (right_edge - left_edge) * x / 22.65, 159.8 + 36.3 * depth + 2.4 * depth**2


@dataclass(frozen=True)
class Key:
    code: str
    legend: str
    x: float
    y: float
    w: float
    h: float

    def cap(self):
        # Center an inset, projecting all four corners of the key independently.
        l, r = self.x + .06, self.x + self.w - .06
        t, b = self.y + .065, self.y + self.h - .065
        return project(r,b), project(l,b), project(l,t), project(r,t)

    def target(self):
        a,b,c,d = self.cap()
        x,y = project(self.x + self.w/2, self.y + self.h/2)
        return x,y-.25,(b[0]-a[0]+c[0]-d[0])/2,c[1]-a[1]-.55


def row(y, start, entries):
    x = start
    for code, legend, *dimensions in entries:
        width, height = (dimensions + [1, 1])[:2]
        yield Key(code, legend, x, y, width, height)
        x += width


def block(first_scan, legends):
    return [(f"{first_scan+i:02X}", legend) for i,legend in enumerate(legends)]


def layout():
    yield from row(0, 0, [("01","Esc")])
    for start, first, last, scan in ((1.75,1,4,0x3b), (6.15,5,8,0x3f), (10.55,9,10,0x43)):
        yield from row(0,start,block(scan,[f"F{n}" for n in range(first,last+1)]))
    yield from row(0,12.55,[("57","F11"),("58","F12")])
    yield from row(0,15.32,[("E037","Prt"),("46","Scr"),("E11D","Pse")])

    # The international extra key fits next to the shorter left Shift.
    yield from row(1.18,0,[("29","`"), *block(0x02,"1234567890-="), ("0E","Back",2)])
    yield from row(2.18,0,[("0F","Tab",1.5), *block(0x10,"QWERTYUIOP[]"), ("2B","\\",1.5)])
    yield from row(3.18,0,[("3A","Caps",1.75), *block(0x1e,"ASDFGHJKL;'"), ("1C","Enter",2.25)])
    yield from row(4.18,0,[("2A","Shift",1.25), ("56","<>"), *block(0x2c,"ZXCVBNM,./"), ("36","Shift",2.75)])
    yield from row(5.18,0,[("1D","Ctrl",1.25),("E05B","Win",1.25),("38","Alt",1.25),("39","",6.25),
                           ("E038","Alt",1.25),("E05C","Win",1.25),("E05D","Menu",1.25),("E01D","Ctrl",1.25)])
    for depth, entries in (
            (1.18,[("E052","Ins"),("E047","Home"),("E049","PgU")]),
            (2.18,[("E053","Del"),("E04F","End"),("E051","PgD")]),
            (5.18,[("E04B","Left"),("E050","Down"),("E04D","Right")])):
        yield from row(depth,15.32,entries)
    yield from row(4.18,16.32,[("E048","Up")])
    for depth, entries in (
            (1.18,[("45","Num"),("E035","/"),("37","*"),("4A","-")]),
            (2.18,[("47","7"),("48","8"),("49","9"),("4E","+",1,2)]),
            (3.18,[("4B","4"),("4C","5"),("4D","6")]),
            (4.18,[("4F","1"),("50","2"),("51","3"),("E01C","Ent",1,2)]),
            (5.18,[("52","0",2),("53",".")])):
        yield from row(depth,18.65,entries)


DECK = tuple(layout())
TARGETS = {key.code: key.target() for key in DECK}
TARGETS.update(MOUSE_BUTTONS)


def key_center(name):
    return TARGETS[KEYS[name]][:2]


def paw_contact(name):
    x,y = key_center(name)
    # The paw's rounded underside sits four cell pixels below its rig origin.
    return cat_coordinates((x, y - 4))
