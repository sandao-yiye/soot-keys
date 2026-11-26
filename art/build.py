"""Regenerate both editions and the README motion preview.

Run: python -m pip install pillow aggdraw && python art/build.py
"""

from pathlib import Path
import csv
import json

from PIL import Image

from desk import TARGETS, cat_point, paw_contact
from illustration import joints, render
from motion import LIVE, ROWS, Pose, pose_for

ROOT = Path(__file__).resolve().parents[1]
PET = ROOT / "pet"
LIVE_DIR = ROOT / "art" / "live"
PREVIEW = ROOT / "preview"


def build_live():
    LIVE_DIR.mkdir(parents=True, exist_ok=True)
    with (LIVE_DIR / "keyboard.csv").open("w", encoding="utf-8", newline="") as dest:
        writer = csv.writer(dest, lineterminator="\n")
        writer.writerow(("id","x","y","width","height"))
        for code, geometry in TARGETS.items():
            writer.writerow([code, *(round(v,3) for v in geometry)])
    default_contacts = {"FL":"tapRight", "FR":"tapLeft", "RL":"enter", "RR":"space"}
    neutral = joints(Pose())
    with (LIVE_DIR / "eyes.csv").open("w", encoding="utf-8", newline="") as dest, \
         (LIVE_DIR / "rig.csv").open("w", encoding="utf-8", newline="") as rig_file:
        writer = csv.writer(dest, lineterminator="\n")
        rig = csv.writer(rig_file, lineterminator="\n")
        rig.writerow(("pose","frame","limb","startX","startY","endX","endY","progress"))
        writer.writerow(("pose", "frame", "leftX", "leftY", "leftRx", "leftRy",
                         "rightX", "rightY", "rightRx", "rightRy"))
        for name in LIVE:
            for i in range(6):
                p = pose_for(name, i / (6 if name == "idle" else 5))
                # Two device pixels per design pixel keep the legends crisp at
                # 150–200% Windows scaling; all joint geometry stays in design units.
                image, geometry = render(p, eyes=False, size=(384,416), live_base=True)
                image.save(LIVE_DIR / f"{name}-{i}.png", optimize=True)
                writer.writerow([name, i, *(round(v, 3) for eye in geometry for v in eye)])
                for limb, (start, end, _) in joints(p).items():
                    action = "mouse" if name == "mouse" and limb == "FL" else default_contacts[limb]
                    progress = 0
                    if name in (action,"combo"):
                        home = neutral[limb][1]
                        target = paw_contact(action)
                        # The vertical component of the interpolated strike is monotonic.
                        progress = (end[1] - home[1]) / (target[1] - home[1])
                    rig.writerow([name,i,limb, *(round(v,4) for v in (
                        *cat_point(*start), *cat_point(*end), progress))])


def build_native():
    PET.mkdir(exist_ok=True)
    sheet = Image.new("RGBA", (1536, 2288))
    for row, (name, count) in enumerate(ROWS):
        for column in range(count):
            if name == "look":
                t = ((row - 9)*8 + column)/16
            elif name == "jump":
                t = column / (count - 1)
            else:
                t = column / count
            p = pose_for(name, t)
            if name == "idle":
                # Codex holds idle cells for about a second. Give the downbeat
                # a slow sleepy squint; desktop mode draws its own fast blink.
                p.open = max(.45, p.open)
            image, _ = render(p)
            assert image.getbbox() and image.getpixel((0, 0))[3] == 0
            sheet.paste(image, (192*column, 208*row))
    transparent = sheet.getchannel("A").point(lambda a: 255 if a == 0 else 0)
    sheet.paste((0, 0, 0, 0), mask=transparent)
    sheet.save(PET / "spritesheet.webp", lossless=True, exact=True, method=6)
    manifest = {
        "id": "soot-keys",
        "displayName": "墨爪",
        "description": "一只穿白袜子的金眼黑猫。前爪在忙，空格键归后脚管。",
        "spriteVersionNumber": 2,
        "spritesheetPath": "spritesheet.webp"
    }
    (PET / "pet.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def build_preview():
    PREVIEW.mkdir(exist_ok=True)
    frames = []
    durations = []
    for name, count, ms in [("idle", 16, 105), ("tapLeft", 7, 45), ("tapRight", 7, 45),
                            ("space", 12, 65), ("enter", 10, 55), ("mouse", 12, 75), ("combo", 16, 80),
                            ("look", 20, 70), ("wave", 10, 90)]:
        for i in range(count):
            t = i / (count if name in ("idle", "look", "wave") else count-1)
            p = pose_for(name, t)
            if name == "idle":
                p.open = min(1, max(.025, abs(t - .69) * 16))
            image, _ = render(p, size=(288, 312))
            # Pale mint paper gives the dark silhouette a fixed, friendly stage.
            stage = Image.new("RGB", (340, 328), "#DCEAE2")
            stage.paste(image, (26, 8), image)
            frames.append(stage)
            durations.append(ms)
    frames[0].save(PREVIEW/"soot-keys.gif", save_all=True, append_images=frames[1:],
                   duration=durations, loop=0, disposal=1, optimize=True)


if __name__ == "__main__":
    build_live()
    build_native()
    build_preview()
