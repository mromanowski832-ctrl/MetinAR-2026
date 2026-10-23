#!/usr/bin/env python3
"""METINAR-041 E1 (#86): print register rows (docs/assets/provenance/*.csv) with the current SHA-256.

Usage (repo root):
  python tools/assets/provenance_rows.py --asset-id ArEncounter --created-with "..." --date 2026-10-20 \
      [--origin original] [--author "MetinAR project"] [--license MetinAR-Owned] [--source-url URL] [--notes "..."] \
      client/Assets/MetinAR/Art/ArEncounter/T_ArReticle.png ...
Paths may be given relative to the repo root or to client/Assets/MetinAR. Output goes to stdout (append it to your
fragment; one fragment per PR/asset set avoids merge conflicts). Check with:
  dotnet test tests/AssetPipeline.Tests --filter AssetProvenance
"""
import argparse, csv, hashlib, os, sys

SCOPE = "client/Assets/MetinAR/"
# Unity-serialized assets are re-saved by editor scripts: their sha256 stays empty (AssetProvenanceRegister.UnitySerializedExtensions).
UNITY_SERIALIZED = {".mat", ".anim", ".controller", ".overridecontroller", ".mask", ".prefab", ".unity", ".mixer", ".physicmaterial",
                    ".rendertexture", ".cubemap", ".spriteatlas", ".spriteatlasv2", ".fontsettings", ".shadergraph", ".shadersubgraph", ".vfx"}
HEADER = ["path", "sha256", "asset_id", "origin", "author", "license", "commercial_use", "created_with", "date_added", "source_url", "notes"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--asset-id", required=True)
    ap.add_argument("--origin", default="original")
    ap.add_argument("--author", default="MetinAR project")
    ap.add_argument("--license", default="MetinAR-Owned")
    ap.add_argument("--created-with", required=True)
    ap.add_argument("--date", required=True)
    ap.add_argument("--source-url", default="")
    ap.add_argument("--notes", default="")
    ap.add_argument("--header", action="store_true", help="print the CSV header first")
    ap.add_argument("files", nargs="+")
    a = ap.parse_args()
    w = csv.writer(sys.stdout, lineterminator="\n")
    if a.header:
        w.writerow(HEADER)
    for f in sorted(x for x in a.files if not x.endswith(".meta")):
        rel = f.replace("\\", "/")
        rel = rel[len(SCOPE):] if rel.startswith(SCOPE) else rel
        with open(os.path.join(SCOPE, rel), "rb") as fh:
            sha = hashlib.sha256(fh.read()).hexdigest()
        if os.path.splitext(rel)[1].lower() in UNITY_SERIALIZED:
            sha = ""
        w.writerow([rel, sha, a.asset_id, a.origin, a.author, a.license, "yes", a.created_with, a.date, a.source_url, a.notes])


if __name__ == "__main__":
    main()
