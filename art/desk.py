"""Shared camera and key positions, in the 192 × 208 sprite cell."""

CAT_SCALE = .89
CAT_ORIGIN = (12, 2)
BOARD_BACK = 160
MOUSE_WHEEL = (14.4, 175.3)
KEYS = {"tapLeft": (2, 2), "tapRight": (2, 7), "space": (3, 3),
        "enter": (2, 9), "combo": (3, 3)}


def cat_point(x, y):
    return CAT_ORIGIN[0] + x * CAT_SCALE, CAT_ORIGIN[1] + y * CAT_SCALE


def cat_coordinates(screen_point):
    return tuple((v - offset) / CAT_SCALE for v, offset in zip(screen_point, CAT_ORIGIN))


def keys():
    # Nearer rows are both wider and deeper. All contacts and the flying cap
    # use these coordinates, including when the rig is rendered at another size.
    rows = (
        (163, 5.2, 42, 169, [1] * 12),
        (170, 5.9, 39, 172, [1.35] + [1] * 9 + [1.5]),
        (178, 6.2, 35, 175.5, [1.6] + [1] * 8 + [2.1]),
        (186.5, 6.9, 31.5, 179, [1.2, 1.2, 1.2, 5.5, 1.1, 1.1, 1.2]),
    )
    for row, (top, height, left, right, widths) in enumerate(rows):
        unit = (right-left) / sum(widths)
        for col, weight in enumerate(widths):
            end = left + weight * unit
            yield (row, col), (left, top, end-1.35, height)
            left = end


def key_center(name):
    left, top, right, height = dict(keys())[KEYS[name]]
    return (left + right)/2, top + height/2


def paw_contact(name):
    x, y = MOUSE_WHEEL if name == "mouse" else key_center(name)
    # The paw's rounded underside sits four cell pixels below its rig origin.
    return cat_coordinates((x, y - 4))
