"""METINAR-032: Unity-free check of the committed item icons (mirrors ItemIconsCheck C02-C06 on the PNG/.meta files).

Usage (repo root):  python art-source/ItemIcons/check_icons.py
Checks for every id in make_icons.ALL_ITEMS: file name pattern of METINAR-009 (T_<SetId>_<Part>_BC.png), 256x256 RGBA,
transparent corners and border, 15-75 % coverage, .meta import settings (Sprite Single, sRGB, alpha is transparency,
Read/Write off, clamp, max 256, ASTC 4x4 overrides on Android and iOS), unique GUIDs in the whole repo, and that every
item id in docs/design/tables (with <classId> expanded from classes_skills.json) has an icon (a table missing from an
older checkout is read from origin/main). Exit code 0 = all PASS.
"""
import csv
import glob
import json
import os
import io
import re
import subprocess
import sys

import numpy as np
from PIL import Image

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_icons import ALL_ITEMS, pascal  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FOLDER = os.path.join(ROOT, "client/Assets/MetinAR/Textures/ItemIcons")
TABLES = os.path.join(ROOT, "docs/design/tables")
NAME_RE = re.compile(r"^T_([A-Z][A-Za-z0-9]*)(?:_([A-Z][A-Za-z0-9]*))?_(BC|N|MS|AO|E|M)\.(png|tga)$")
checks = failures = 0


def check(name, ok, detail=""):
    global checks, failures
    checks += 1
    failures += 0 if ok else 1
    print(("PASS " if ok else "FAIL ") + name + (" — " + detail if detail else ""))
    return ok


def meta_ok(text):
    def platform(target):
        m = re.search(r"buildTarget: %s\n(.*?)(?:\n  - serializedVersion|\n  spriteSheet)" % target, text, re.S)
        return m.group(1) if m else ""
    want = ["textureType: 8", "spriteMode: 1", "sRGBTexture: 1", "alphaIsTransparency: 1", "isReadable: 0",
            "wrapU: 1", "wrapV: 1", "enableMipMap: 1"]
    miss = [w for w in want if ("  " + w) not in text]
    for t in ("Android", "iOS"):
        p = platform(t)
        if not ("maxTextureSize: 256" in p and "textureFormat: 48" in p and "overridden: 1" in p):
            miss.append(t + " ASTC 4x4 @256 override")
    if "maxTextureSize: 256" not in platform("DefaultTexturePlatform"):
        miss.append("default max 256")
    return miss


def table(name):
    """Table from the working tree; on a branch cut before the table landed on main, read it from origin/main."""
    path = os.path.join(TABLES, name)
    if os.path.exists(path):
        return open(path, encoding="utf-8").read()
    rel = "docs/design/tables/" + name
    text = subprocess.run(["git", "-C", ROOT, "show", "origin/main:" + rel], capture_output=True, text=True,
                          encoding="utf-8", check=True).stdout
    print("INFO %s not in this checkout, read from origin/main" % rel)
    return text


def main():
    for item in ALL_ITEMS:
        name = "T_ItemIcons_%s_BC.png" % pascal(item)
        path = os.path.join(FOLDER, name)
        check("N01 %s: name matches METINAR-009 texture pattern" % item, NAME_RE.match(name) is not None, name)
        if not check("F01 %s: PNG and .meta present" % item, os.path.exists(path) and os.path.exists(path + ".meta")):
            continue
        im = Image.open(path)
        a = np.asarray(im.convert("RGBA"))[..., 3]
        check("C02 %s: 256x256 RGBA" % item, im.size == (256, 256) and im.mode == "RGBA", "%s %s" % (im.size, im.mode))
        border = int(max(a[0].max(), a[-1].max(), a[:, 0].max(), a[:, -1].max()))
        corners = int(max(a[0, 0], a[0, -1], a[-1, 0], a[-1, -1]))
        cov = float((a > 128).mean())
        check("C06 %s: transparent corners/border, coverage 15-75 %%" % item,
              corners == 0 and border < 8 and 0.15 < cov < 0.75, "coverage %.1f %%, border alpha %d" % (cov * 100, border))
        text = open(path + ".meta").read()
        miss = meta_ok(text)
        sprite = re.search(r"name: (T_ItemIcons_\w+_BC_0)", text)
        check("C03/C04 %s: .meta Sprite/sRGB/alpha/RW off/clamp/ASTC" % item,
              not miss and sprite is not None and sprite.group(1) == name[:-4] + "_0", ", ".join(miss) or "ok")
    guids = {}
    for m in glob.glob(os.path.join(ROOT, "**", "*.meta"), recursive=True):
        g = re.search(r"^guid: ([0-9a-f]{32})", open(m, errors="ignore").read(), re.M)
        if g:
            guids.setdefault(g.group(1), []).append(os.path.relpath(m, ROOT))
    dup = {g: p for g, p in guids.items() if len(p) > 1}
    check("G01 unique .meta GUIDs in the repository", not dup, "%d metas, %d duplicates %s" % (len(guids), len(dup), list(dup.values())[:3]))
    classes = [c["id"] for c in json.loads(table("classes_skills.json"))["classes"]]
    ids = set()
    for f in ("items_starter.csv", "shop_starter_merchant.csv", "skill_tier_items.csv"):
        for row in csv.DictReader(io.StringIO(table(f))):
            i = row["itemId"]
            ids |= {i.replace("<classId>", c) for c in classes} if "<classId>" in i else {i}
    missing = sorted(ids - set(ALL_ITEMS))
    check("T01 every item id in docs/design/tables has an icon", not missing, "%d ids, missing %s" % (len(ids), missing))
    extra = sorted(set(ALL_ITEMS) - ids)
    check("T02 no icon without an item id in docs/design/tables", not extra, str(extra) if extra else "")
    print("[METINAR-032] box icon check: checks=%d failures=%d" % (checks, failures))
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
