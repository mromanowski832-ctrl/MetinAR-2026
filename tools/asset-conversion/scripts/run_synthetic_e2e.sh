#!/usr/bin/env bash
# End-to-end pipeline test on SYNTHETIC data only (no Metin2/Arezzo content).
# Env: BLENDER (path to blender), GR2_ADDON_DIR (folder with io_scene_gr2/), PYTHON (with Pillow), WORK (output dir)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
: "${BLENDER:?set BLENDER}"; : "${GR2_ADDON_DIR:?set GR2_ADDON_DIR}"
PYTHON="${PYTHON:-python3}"; WORK="${WORK:-/tmp/metinar-import-synthetic}"
rm -rf "$WORK"; mkdir -p "$WORK"
log() { echo "[$(date -u +%FT%TZ)] $*" | tee -a "$WORK/e2e.log"; }
log "1/6 make synthetic set"
"$BLENDER" -b --factory-startup --python "$HERE/blender_make_synthetic.py" -- "$WORK/src" >>"$WORK/e2e.log" 2>&1
"$PYTHON" - "$WORK/src" <<'PY'
import sys; from PIL import Image
d=sys.argv[1]; Image.open(f"{d}/synthetic_texture.png").convert("RGBA").save(f"{d}/synthetic_texture.dds", pixel_format="DXT5")
PY
rm -f "$WORK/src/synthetic_texture.png" "$WORK/src"/*.export.json   # the pipeline must start from .gr2/.dds/.msm/.msa only
log "2/6 manifest"
"$PYTHON" "$HERE/build_manifest.py" "$WORK/src" "$WORK/manifest" --tool-versions "synthetic" >"$WORK/manifest_summary.json"
log "3/6 gr2 probe"
"$PYTHON" "$HERE/gr2_probe.py" --json "$WORK"/src/*.gr2 >"$WORK/gr2_probe.json"
log "4/6 dds -> png"
"$PYTHON" "$HERE/dds_to_png.py" "$WORK/out/Textures" "$WORK/src/synthetic_texture.dds" --json >"$WORK/textures.json"
log "5/6 gr2 -> fbx"
"$BLENDER" -b --factory-startup --python "$HERE/blender_gr2_to_fbx.py" -- \
  --model "$WORK/src/synthetic_model.gr2" --id SyntheticTest --category Mob --out "$WORK/out/Models" \
  --clip Idle="$WORK/src/synthetic_wait.gr2" --clip Walk="$WORK/src/synthetic_run.gr2" \
  --clip Attack="$WORK/src/synthetic_attack.gr2" >>"$WORK/e2e.log" 2>&1
log "6/6 verify fbx"
"$BLENDER" -b --factory-startup --python "$HERE/blender_verify_fbx.py" -- "$WORK/verify.json" "$WORK"/out/Models/*.fbx >>"$WORK/e2e.log" 2>&1
"$PYTHON" - "$WORK" <<'PY'
import json, sys
w = sys.argv[1]
rep = json.load(open(f"{w}/out/Models/convert_report.json"))
ver = json.load(open(f"{w}/verify.json"))
tex = json.load(open(f"{w}/textures.json"))
errs = rep["errors"] + [f"{v['file']}: {e}" for v in ver for e in v["errors"]] + [t["error"] for t in tex if "error" in t]
print(json.dumps({"convert": {k: rep[k] for k in ("triangles", "bones", "materials", "armatures", "meshes")},
                  "clips": [(c["state"], c["frames"], c["fcurves"]) for c in rep["clips"]],
                  "verify": [(v["file"], v["triangles"], len(v["bones"]), v["height_m"], v["ground_offset_m"], [a["frames"] for a in v["actions"] if a["fcurves"]]) for v in ver],
                  "textures": [(t.get("width"), t.get("height"), t.get("dds_format"), t.get("alpha_used"), t.get("alpha_binary")) for t in tex],
                  "errors": errs}, indent=1))
sys.exit(1 if errs else 0)
PY
log "7/7 deformation fidelity vs Blender reference (KD-tree, metres)"
for pair in Idle:wait Walk:run Attack:attack; do
  st="${pair%%:*}"; src="${pair##*:}"
  "$BLENDER" -b --factory-startup --python "$HERE/blender_compare_clip.py" -- "$WORK/src/synthetic_ref_${src}.fbx" "$WORK/out/Models/A_SyntheticTest_${st}.fbx" 2>/dev/null \
    | sed -n "s/^COMPARE /${st} /p" | tee -a "$WORK/fidelity.txt"
done
log "E2E PASS (structure); fidelity numbers in fidelity.txt"
