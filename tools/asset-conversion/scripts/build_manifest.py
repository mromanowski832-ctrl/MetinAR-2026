#!/usr/bin/env python3
"""Build a CSV + JSON manifest of an extracted asset workshop folder.

Detects real format by magic bytes (not only extension), hashes every file,
de-duplicates by content, keeps name collisions as separate variants and links
.msm/.msa scripts to the .gr2/.dds files they reference.

Read-only on SRC_ROOT. Output files only in OUT_DIR.

Usage: build_manifest.py SRC_ROOT OUT_DIR [--tool-versions "blender=4.2.9;io_scene_gr2=0.1.61"]
"""
from __future__ import annotations

import csv
import hashlib
import json
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gr2_probe  # noqa: E402
import m2script  # noqa: E402

COLUMNS = [
    "source_relative_path", "sha256", "size", "detected_type", "ext", "set_key", "name_or_id",
    "model", "skeleton", "textures", "clips", "effects", "variant_of", "dependencies",
    "missing", "target_files", "tools", "status", "notes",
]


def sha256(p: Path) -> str:
    h = hashlib.sha256()
    with p.open("rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def detect(p: Path) -> tuple[str, str]:
    with p.open("rb") as fh:
        head = fh.read(64)
    if head[:16] in gr2_probe.MAGIC:
        try:
            r = gr2_probe.probe_file(p)
            return "gr2", f"{r['format']} v{r['version']} {','.join(r['compressions_used']) or 'none'}"
        except gr2_probe.Gr2ProbeError as exc:
            return "gr2-invalid", str(exc)
    if head[:4] == b"DDS ":
        return "dds", ""
    if head[:8] == b"\x89PNG\r\n\x1a\n":
        return "png", ""
    if head[:4] == b"MCOZ":
        return "packed-encrypted(MCOZ)", "compressed+encrypted pack block"
    if head[:4] == b"EPKD":
        return "eterpack-index", ""
    text = head.lstrip()
    if text.startswith(b"ScriptType") or b"ScriptType" in head:
        return "m2script", ""
    if p.suffix.lower() == ".tga":
        return "tga", "by extension"
    return "other", ""


def set_key(rel: Path) -> str:
    # Metin2 convention: one folder per model set (e.g. ymir work/monster2/spider_poison)
    return rel.parent.as_posix().lower()


def build(src_root: Path, out_dir: Path, tools: str = "") -> dict:
    out_dir.mkdir(parents=True, exist_ok=True)
    files = sorted(p for p in src_root.rglob("*") if p.is_file())
    rows, by_hash, by_name = [], defaultdict(list), defaultdict(list)
    for p in files:
        rel = p.relative_to(src_root)
        typ, note = detect(p)
        digest = sha256(p)
        row = {c: "" for c in COLUMNS}
        row.update(source_relative_path=rel.as_posix(), sha256=digest, size=p.stat().st_size,
                   detected_type=typ, ext=p.suffix.lower(), set_key=set_key(rel), tools=tools,
                   notes=note, status="INVENTORIED")
        if typ == "m2script":
            try:
                parsed = m2script.parse_file(p)
                s = parsed["summary"]
                refs = s["references"]
                row["dependencies"] = ";".join(refs)
                row["model"] = s.get("base_model", "") if isinstance(s.get("base_model"), str) else ""
                row["clips"] = s.get("motion_file", "") if isinstance(s.get("motion_file"), str) else ""
                row["effects"] = ";".join(r for r in refs if r.lower().endswith(".mse"))
                row["notes"] = f"ScriptType={s['script_type']}"
            except m2script.M2ScriptError as exc:
                row.update(status="PARSE_ERROR", notes=str(exc))
        by_hash[digest].append(row)
        by_name[rel.name.lower()].append(row)
        rows.append(row)
    # duplicates by content
    for group in by_hash.values():
        for r in group[1:]:
            r["variant_of"] = f"DUPLICATE_OF:{group[0]['source_relative_path']}"
    # same name, different content -> variants
    for name, group in by_name.items():
        hashes = {r["sha256"] for r in group}
        if len(hashes) > 1:
            for r in group:
                if not r["variant_of"]:
                    r["variant_of"] = f"NAME_COLLISION:{name}:{len(hashes)}_variants"
    # per-set aggregation + dependency check (script references resolved by basename inside the set)
    present = defaultdict(set)
    for r in rows:
        present[r["set_key"]].add(Path(r["source_relative_path"]).name.lower())
    for r in rows:
        if r["dependencies"]:
            missing = [d for d in r["dependencies"].split(";")
                       if Path(d).name.lower() not in present[r["set_key"]]
                       and not any(Path(d).name.lower() in names for names in present.values())]
            r["missing"] = ";".join(missing)
            if missing:
                r["status"] = "MISSING_DEPENDENCY"
    sets = defaultdict(lambda: defaultdict(list))
    for r in rows:
        sets[r["set_key"]][r["detected_type"]].append(r["source_relative_path"])
    with (out_dir / "manifest.csv").open("w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=COLUMNS)
        w.writeheader()
        w.writerows(rows)
    summary = {
        "source_root": str(src_root), "files": len(rows),
        "unique_contents": len(by_hash),
        "by_type": {t: sum(1 for r in rows if r["detected_type"] == t) for t in sorted({r["detected_type"] for r in rows})},
        "sets": {k: {t: len(v) for t, v in d.items()} for k, d in sorted(sets.items())},
        "missing_dependencies": [r["source_relative_path"] for r in rows if r["status"] == "MISSING_DEPENDENCY"],
    }
    (out_dir / "manifest.json").write_text(json.dumps({"summary": summary, "files": rows}, indent=2), encoding="utf-8")
    return summary


def main(argv: list[str]) -> int:
    tools = ""
    if "--tool-versions" in argv:
        i = argv.index("--tool-versions")
        tools = argv[i + 1]
        argv = argv[:i] + argv[i + 2:]
    if len(argv) != 2:
        print(__doc__)
        return 2
    print(json.dumps(build(Path(argv[0]), Path(argv[1]), tools), indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
