#!/usr/bin/env bash
# Download and verify the conversion toolchain into $TOOLS_DIR (no root, nothing system-wide).
# Versions/hashes pinned; re-running is idempotent.
set -euo pipefail
TOOLS_DIR="${TOOLS_DIR:-$HOME/tools}"
mkdir -p "$TOOLS_DIR"; cd "$TOOLS_DIR"
BLENDER_VER=4.2.9
BLENDER_TAR="blender-${BLENDER_VER}-linux-x64.tar.xz"
BLENDER_SHA=dfbc127a7d28f9c2175b23bf9d6701b2855f31eedfb391f9a6e60adb24572846   # official blender.org .sha256
GR2_VER=0.1.61
GR2_ZIP="io_scene_gr2-${GR2_VER}.zip"
GR2_SHA=21828d8fc4cfed615b75b41d72c8c0458165c7ef482bf9b094f1da36b31c5665      # GitHub release asset, recorded 2026-10-21
[ -f "$BLENDER_TAR" ] || curl -fsSLo "$BLENDER_TAR" "https://download.blender.org/release/Blender4.2/${BLENDER_TAR}"
echo "${BLENDER_SHA}  ${BLENDER_TAR}" | sha256sum -c -
[ -d "blender-${BLENDER_VER}-linux-x64" ] || tar xf "$BLENDER_TAR"
[ -f "$GR2_ZIP" ] || curl -fsSLo "$GR2_ZIP" "https://github.com/Rasetsuu/blendergranny/releases/download/v${GR2_VER}/${GR2_ZIP}"
echo "${GR2_SHA}  ${GR2_ZIP}" | sha256sum -c -
rm -rf gr2addon && mkdir gr2addon && (cd gr2addon && unzip -q "../$GR2_ZIP")
[ -d venv ] || python3 -m venv venv
venv/bin/pip install -q "Pillow>=10" pytest
cat <<EOF
export BLENDER=$TOOLS_DIR/blender-${BLENDER_VER}-linux-x64/blender
export GR2_ADDON_DIR=$TOOLS_DIR/gr2addon
export PYTHON=$TOOLS_DIR/venv/bin/python
EOF
