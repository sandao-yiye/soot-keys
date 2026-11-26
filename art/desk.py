"""Shared camera and key positions, in the 192 × 208 sprite cell."""

CAT_SCALE = .89
CAT_ORIGIN = (12, 2)
BOARD_BACK = 160
MOUSE_WHEEL = (14.4, 175.3)
KEYS = {"tapLeft": (2, 2), "tapRight": (2, 7), "space": (3, 3), "enter": (2, 9)}


def cat_point(x, y):
    return CAT_ORIGIN[0] + x * CAT_SCALE, CAT_ORIGIN[1] + y * CAT_SCALE


def cat_coordinates(screen_point):
    return tuple((v - offset) / CAT_SCALE for v, offset in zip(screen_point, CAT_ORIGIN))


def keys():
    # Logical indices are from the typist's (cat's) seat. Both axes run
    # opposite the screen axes. Keep the case and near-side cap walls in
    # camera perspective while placing rows and legends on the rotated deck.
    widths_by_row = (
        [1] * 12,
        [1.35] + [1] * 9 + [1.5],
        [1.6] + [1] * 8 + [2.1],
        [1.2, 1.2, 1.2, 5.5, 1.1, 1.1, 1.2],
    )
    projected_rows = (
        (163, 5.2, 42, 169),
        (170, 5.9, 39, 172),
        (178, 6.2, 35, 175.5),
        (186.5, 6.9, 31.5, 179),
    )
    for row, (top, height, left, right) in zip(range(3, -1, -1), projected_rows):
        widths = widths_by_row[row]
        unit = (right-left) / sum(widths)
        for col in reversed(range(len(widths))):
            end = left + widths[col] * unit
            yield (row, col), (left, top, end-1.35, height)
            left = end


def key_center(name):
    left, top, right, height = dict(keys())[KEYS[name]]
    return (left + right)/2, top + height/2


def paw_contact(name):
    x, y = MOUSE_WHEEL if name == "mouse" else key_center(name)
    # The paw's rounded underside sits four cell pixels below its rig origin.
    return cat_coordinates((x, y - 4))
