#!/usr/bin/env python3
"""Convert DDS/TGA textures to PNG (lossless) and report alpha/size facts.

Never overwrites an existing output unless --overwrite; never writes next to the
source unless the output dir says so. Requires Pillow (>= 10).

Usage: dds_to_png.py OUT_DIR SRC [SRC ...] [--overwrite] [--json]
"""
from __future__ import annotations

import hashlib
import json
import struct
import sys
from pathlib import Path

from PIL import Image


def dds_fourcc(path: Path) -> str | None:
    with path.open("rb") as fh:
        head = fh.read(128)
    if head[:4] != b"DDS " or len(head) < 128:
        return None
    flags = struct.unpack_from("<I", head, 80)[0]
    fourcc = head[84:88]
    if flags & 0x4:
        return fourcc.decode("ascii", "replace")
    bits = struct.unpack_from("<I", head, 88)[0]
    return f"RGB{bits}" + ("A" if flags & 0x1 else "")


def is_pow2(v: int) -> bool:
    return v > 0 and (v & (v - 1)) == 0


def convert(src: Path, out_dir: Path, overwrite: bool = False) -> dict:
    out_dir.mkdir(parents=True, exist_ok=True)
    dst = out_dir / (src.stem + ".png")
    if dst.exists() and not overwrite:
        raise FileExistsError(f"{dst} exists (use --overwrite)")
    info: dict = {"source": str(src), "source_sha256": hashlib.sha256(src.read_bytes()).hexdigest(),
                  "dds_format": dds_fourcc(src)}
    with Image.open(src) as im:
        im.load()
        info.update(mode_in=im.mode, width=im.width, height=im.height)
        has_alpha_channel = im.mode in ("RGBA", "LA") or "transparency" in im.info
        rgba = im.convert("RGBA") if has_alpha_channel else im.convert("RGB")
        alpha_used = False
        alpha_binary = None
        if has_alpha_channel:
            lo, hi = rgba.getchannel("A").getextrema()
            alpha_used = lo < 255
            hist = rgba.getchannel("A").histogram()
            alpha_binary = alpha_used and sum(hist[1:255]) == 0
        if not alpha_used:
            rgba = rgba.convert("RGB")
        tmp = dst.with_suffix(".png.tmp")
        rgba.save(tmp, format="PNG", optimize=False)
        tmp.replace(dst)
    info.update(
        output=str(dst),
        output_sha256=hashlib.sha256(dst.read_bytes()).hexdigest(),
        alpha_used=alpha_used,
        alpha_binary=alpha_binary,  # True -> URP alpha clipping; False -> real translucency (review)
        power_of_two=is_pow2(info["width"]) and is_pow2(info["height"]),
        color_space_hint="sRGB (base colour)" ,
    )
    return info


def main(argv: list[str]) -> int:
    flags = {a for a in argv if a.startswith("--")}
    args = [a for a in argv if not a.startswith("--")]
    if len(args) < 2:
        print(__doc__)
        return 2
    out_dir = Path(args[0])
    results, rc = [], 0
    for s in args[1:]:
        try:
            results.append(convert(Path(s), out_dir, overwrite="--overwrite" in flags))
        except Exception as exc:  # report and continue with the batch
            results.append({"source": s, "error": f"{type(exc).__name__}: {exc}"})
            rc = 1
    if "--json" in flags:
        print(json.dumps(results, indent=2))
    else:
        for r in results:
            print(r.get("error") or f"{r['source']} -> {r['output']} {r['width']}x{r['height']} "
                  f"{r['dds_format']} alpha={r['alpha_used']}")
    return rc


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
