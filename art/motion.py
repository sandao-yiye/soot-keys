"""Pose choreography. Coordinates are screen pixels, +x right, +y down."""

from dataclasses import dataclass
from math import cos, pi, sin

from desk import paw_contact


@dataclass
class Pose:
    # Torso translation, head turn, eyelid openness, and tail tip travel.
    x: float = 0
    y: float = 0
    head_x: float = 0
    head_y: float = 0
    tilt: float = 0
    look_x: float = 0
    look_y: float = 0
    open: float = 1
    tail: float = 0
    jaw: float = 0
    front_l: tuple = (76, 139)
    front_r: tuple = (117, 139)
    rear_l: tuple = (65, 162)
    rear_r: tuple = (125, 162)
    key: str = ""
    press: float = 0
    keycap_time: float = 0


LIVE = ("idle", "tapLeft", "tapRight", "space", "enter", "mouse", "combo")
ROWS = (("idle", 6), ("runRight", 8), ("runLeft", 8), ("wave", 4),
        ("jump", 5), ("failed", 8), ("waiting", 6), ("working", 6),
        ("review", 6), ("look", 8), ("look", 8))


def reach(start, end, amount):
    return tuple(a + (b - a) * amount for a, b in zip(start, end))


def pose_for(name, t):
    """Periodic t (0..1); tap clips are finite, starting and ending at rest."""
    p = Pose()
    phase = t * 2 * pi
    beat = sin(phase)
    down = (1 - cos(phase)) / 2
    p.tail = 4 * beat

    if name == "look":
        p.look_x, p.look_y = sin(phase), -cos(phase)
        p.head_x, p.head_y = 6 * p.look_x, 5 * p.look_y
        p.tilt = p.look_x * 4
        p.x = 1.6 * p.look_x
        p.tail = 0
    elif name == "idle":
        p.y = -2.6 * down
        p.head_y = -1.8 * down
        p.front_l = (76, 139 - down * 2.6)
        p.front_r = (117, 139 - down * 2.6)
        p.look_x = -0.15 + 0.32 * beat
        p.look_y = 0.18
        p.open = max(0.03, min(1, abs(t - 4 / 6) * 9))
    elif name in ("tapLeft", "tapRight", "space", "enter", "mouse"):
        down = min(1, down / .9)
        # Rig suffixes are screen sides. The cat faces the viewer, so keys
        # on the typist's right (including Return) use the screen-left paw.
        side = -1 if name in ("tapRight", "enter", "mouse") else 1
        p.x, p.y = side * down * 4, down * 3
        p.tilt, p.head_x = side * down * 7, side * down * 3
        p.look_x, p.look_y = side * down * .7, down * .85
        p.tail += down * 5
        p.key, p.press = name, down
        if name == "space":
            p.rear_r = reach(p.rear_r, paw_contact(name), down)
            p.front_l = reach(p.front_l, (57, 122), down)
            p.front_r = reach(p.front_r, (131, 129), down)
            p.jaw = down * .4
        elif name == "enter":
            p.rear_l = reach(p.rear_l, paw_contact(name), down)
            p.front_l = reach(p.front_l, (55, 124), down)
        elif name == "mouse":
            p.x, p.tilt = -8 * down, -10 * down
            p.front_l = reach(p.front_l, paw_contact(name), down)
        elif name == "tapRight":
            p.front_l = reach(p.front_l, paw_contact(name), down)
            p.front_r = reach(p.front_r, (123, 135), down)
        else:
            p.front_r = reach(p.front_r, paw_contact(name), down)
            p.front_l = reach(p.front_l, (69, 134), down)
    elif name in ("combo", "working"):
        # Two paired downbeats. A back foot steals the space bar.
        a = down
        b = min(1, (1 - cos(phase * 2)) / 1.8) if name == "combo" else 1 - a
        p.x, p.y, p.tilt = 5 * beat, 3 * abs(beat), 8 * beat
        p.front_r = reach(p.front_r, paw_contact("tapLeft"), a)
        p.front_l = reach(p.front_l, paw_contact("tapRight"), b)
        p.rear_r = reach(p.rear_r, paw_contact("space"), a)
        p.rear_l = reach(p.rear_l, paw_contact("enter"), b)
        p.look_x, p.look_y = beat * .65, .85
        p.key, p.press = ("space", a) if a >= b else ("enter", b)
        p.tail = beat * 11
        p.jaw = down * .8
        if name == "combo":
            p.keycap_time = t
    elif name in ("runRight", "runLeft"):
        side = 1 if name == "runRight" else -1
        p.x, p.y = side * 4, -4 * abs(beat)
        p.tilt, p.head_x = side * 9, side * 5
        p.look_x, p.look_y = side * .97, -.02
        p.front_l = (72 + 14 * beat, 144 - 14 * max(0, beat))
        p.front_r = (119 - 14 * beat, 144 - 14 * max(0, -beat))
        p.rear_l = (68 - 10 * beat, 161 - 14 * max(0, -beat))
        p.rear_r = (125 + 12 * beat, 161 - 14 * max(0, beat))
        p.tail = side * 8 + 6 * beat
    elif name == "wave":
        p.tilt, p.head_x = -6, -2
        p.front_r = (141 + 11 * beat, 89 - down * 9)
        p.front_l = (75, 145)
        p.look_x, p.look_y = .4, -.5
    elif name == "jump":
        lift = down
        p.y, p.head_y = -17 * lift, -1 * lift
        p.front_l = reach(p.front_l, (46, 94), lift)
        p.front_r = reach(p.front_r, (144, 93), lift)
        p.rear_l = reach(p.rear_l, (56, 140), lift)
        p.rear_r = reach(p.rear_r, (137, 137), lift)
        p.tail = -11 * lift
        p.look_y, p.jaw = -lift * .7, lift
    elif name == "failed":
        # Slump behind the board, with only the two paws flopping onto it.
        p.x, p.y, p.tilt = -4 * down, 11 * down, -13 * down
        p.head_y, p.open = 9 * down, 1 - .94 * down
        p.front_l = reach(p.front_l, paw_contact("tapRight"), down)
        p.front_r = reach(p.front_r, paw_contact("tapLeft"), down)
        p.tail, p.look_y = 15 * down, .6
    elif name == "waiting":
        p.tilt, p.head_x, p.head_y = -7 - 3 * down, -2, -3 * down
        p.look_x, p.look_y = -.72 - .15 * down, -.47
        p.front_l = (74, 125 - 3 * down)
        p.front_r = (111, 135 - 2 * down)
    elif name == "review":
        p.x, p.y, p.tilt = -4 - 2 * down, 4 + 2 * down, -9 - 3 * down
        p.look_x, p.look_y = -.85 + .5 * down, .85
        p.open = .77 + .15 * beat
        p.front_l = (70, 103 + down * 2)
        p.front_r = (116 + 4 * beat, 153)
    return p
