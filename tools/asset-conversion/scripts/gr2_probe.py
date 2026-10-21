#!/usr/bin/env python3
"""Read-only Granny2 (.gr2) header probe.

Reports magic/format, file-format version, section table and per-section
compression, so the right import route can be chosen BEFORE any conversion.
Pure Python, no Granny SDK/DLL, never modifies the input.

Usage: gr2_probe.py FILE.gr2 [FILE.gr2 ...] [--json]
"""
from __future__ import annotations

import json
import struct
import sys
from pathlib import Path

# 16-byte Granny magic signatures (public knowledge, e.g. LSLib Magic.cs / OpenGr2 docs).
# The file-format version (6/7) is read from the header, not inferred from the signature.
MAGIC = {
    bytes.fromhex("B867B0CAF86DB10F84728C7E5E19001E"): "LE32-alt",
    bytes.fromhex("29DE6CC0BAA4532B25F5B7A5F666E2EE"): "LE32",
    bytes.fromhex("E59B495E6F631F141E13EBA990BEEDC4"): "LE64",
}
COMPRESSION = {0: "none", 1: "Oodle0", 2: "Oodle1", 3: "BitKnit1", 4: "BitKnit2"}
SECTION_FIELDS = (
    "compression", "offset", "compressed_size", "uncompressed_size", "alignment",
    "first16bit", "first8bit", "relocations_offset", "relocations_count",
    "mixed_marshalling_offset", "mixed_marshalling_count",
)


class Gr2ProbeError(ValueError):
    pass


def probe_bytes(data: bytes) -> dict:
    if len(data) < 32:
        raise Gr2ProbeError("file shorter than the 32-byte Granny magic block")
    signature = data[:16]
    fmt = MAGIC.get(signature)
    if fmt is None:
        raise Gr2ProbeError(f"unknown Granny magic {signature.hex().upper()}")
    if fmt.startswith("BE"):
        raise Gr2ProbeError("big-endian files are not supported by this probe")
    headers_size, header_format, _r1, _r2 = struct.unpack_from("<4I", data, 16)
    pos = 32
    if len(data) < pos + 0x38:
        raise Gr2ProbeError("truncated file header")
    (version, file_size, crc, sections_offset, num_sections,
     root_type_sec, root_type_off, root_node_sec, root_node_off, tag) = struct.unpack_from("<10I", data, pos)
    extra_tags = list(struct.unpack_from("<4I", data, pos + 40))
    if version not in (6, 7):
        raise Gr2ProbeError(f"unsupported Granny file version {version} (expected 6 or 7)")
    sec_base = pos + sections_offset
    sections = []
    for i in range(num_sections):
        off = sec_base + i * 44
        if off + 44 > len(data):
            raise Gr2ProbeError(f"section header {i} beyond end of file")
        values = struct.unpack_from("<11I", data, off)
        sec = dict(zip(SECTION_FIELDS, values))
        sec["index"] = i
        sec["compression_name"] = COMPRESSION.get(sec["compression"], f"unknown({sec['compression']})")
        sections.append(sec)
    used = sorted({s["compression_name"] for s in sections if s["compressed_size"] or s["uncompressed_size"]})
    return {
        "format": fmt,
        "header_format": header_format,
        "headers_size": headers_size,
        "version": version,
        "file_size_declared": file_size,
        "file_size_actual": len(data),
        "crc": f"{crc:08X}",
        "tag": f"{tag:08X}",
        "extra_tags": [f"{t:08X}" for t in extra_tags],
        "num_sections": num_sections,
        "root_type": [root_type_sec, root_type_off],
        "root_node": [root_node_sec, root_node_off],
        "sections": sections,
        "compressions_used": used,
        "size_matches": file_size == len(data),
    }


def probe_file(path: str | Path) -> dict:
    p = Path(path)
    result = probe_bytes(p.read_bytes())
    result["path"] = str(p)
    return result


def main(argv: list[str]) -> int:
    as_json = "--json" in argv
    files = [a for a in argv if a != "--json"]
    if not files:
        print(__doc__)
        return 2
    rc = 0
    out = []
    for f in files:
        try:
            r = probe_file(f)
        except (OSError, Gr2ProbeError) as exc:
            r = {"path": f, "error": str(exc)}
            rc = 1
        out.append(r)
        if not as_json:
            if "error" in r:
                print(f"{f}: ERROR {r['error']}")
            else:
                print(f"{f}: {r['format']} v{r['version']} tag={r['tag']} sections={r['num_sections']} "
                      f"compression={','.join(r['compressions_used']) or 'none'} size_ok={r['size_matches']}")
    if as_json:
        print(json.dumps(out, indent=2))
    return rc


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
