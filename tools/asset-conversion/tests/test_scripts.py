"""Pure-Python tests on SYNTHETIC data only (no Metin2/Arezzo files)."""
import struct
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import build_manifest  # noqa: E402
import gr2_probe  # noqa: E402
import m2script  # noqa: E402

MSA = '''ScriptType               MotionData

MotionFileName           "d:/ymir work/test/wait.gr2"
MotionDuration           2.000000
Accumulation             0.00 0.00 0.00
'''

MSM = '''ScriptType   CharacterModelScript  # comment
BaseModelFileName "d:/ymir work/test/test.gr2"
Group ModelData
{
    Group Texture00
    {
        FileName "d:/ymir work/test/test.dds"
    }
    Group Texture01 {
        FileName "d:/ymir work/test/test_2.dds"
    }
    List Effects
    {
        "d:/ymir work/effect/a.mse"
        "d:/ymir work/effect/b b.mse"
    }
}
'''


def synthetic_gr2(version=7, sections=2, compression=0):
    sig = bytes.fromhex("29DE6CC0BAA4532B25F5B7A5F666E2EE")
    hdr_size = 0x48 if version >= 7 else 0x38
    body = struct.pack("<10I", version, 0, 0, hdr_size, sections, 0, 0, 0, 0, 0x80000037) + struct.pack("<4I", 0, 0, 0, 0)
    if version >= 7:
        body += struct.pack("<4I", 0, 0, 0, 0)
    secs = b"".join(struct.pack("<11I", compression, 0, 10, 10, 4, 0, 0, 0, 0, 0, 0) for _ in range(sections))
    data = sig + struct.pack("<4I", 0, 0, 0, 0) + body + secs
    return data[:20] + data[20:24] + data[24:]


def test_m2script_msa():
    t = m2script.parse_text(MSA)
    assert t["ScriptType"] == "MotionData"
    assert t["MotionDuration"] == 2.0
    assert t["Accumulation"] == [0.0, 0.0, 0.0]
    assert m2script.summarize(t)["motion_file"] == "d:/ymir work/test/wait.gr2"


def test_m2script_msm_groups_lists_and_refs():
    t = m2script.parse_text(MSM)
    md = t["ModelData"]
    assert md["Texture00"]["FileName"].endswith("test.dds")
    assert md["Texture01"]["FileName"].endswith("test_2.dds")
    assert md["Effects"] == ["d:/ymir work/effect/a.mse", "d:/ymir work/effect/b b.mse"]
    refs = m2script.referenced_files(t)
    assert refs[0].endswith("test.gr2") and len(refs) == 5


def test_m2script_errors():
    with pytest.raises(m2script.M2ScriptError):
        m2script.parse_text("Group A\n{\n Key 1\n")
    with pytest.raises(m2script.M2ScriptError):
        m2script.parse_text("}\n")


def test_m2script_repeated_keys_become_list():
    t = m2script.parse_text("Key 1\nKey 2\nKey 3\n")
    assert list(t["Key"]) == [1, 2, 3]


def test_gr2_probe_synthetic_header():
    data = synthetic_gr2()
    r = gr2_probe.probe_bytes(data)
    assert r["version"] == 7 and r["num_sections"] == 2
    assert r["compressions_used"] == ["none"]


def test_gr2_probe_reports_compression_and_rejects_bad_magic():
    r = gr2_probe.probe_bytes(synthetic_gr2(compression=2))
    assert r["compressions_used"] == ["Oodle1"]
    with pytest.raises(gr2_probe.Gr2ProbeError):
        gr2_probe.probe_bytes(b"\0" * 64)
    with pytest.raises(gr2_probe.Gr2ProbeError):
        gr2_probe.probe_bytes(synthetic_gr2(version=5))


def test_manifest_dedup_variants_and_missing(tmp_path):
    src = tmp_path / "src"
    (src / "monster" / "a").mkdir(parents=True)
    (src / "monster" / "b").mkdir(parents=True)
    (src / "monster" / "a" / "a.msm").write_text(MSM.replace("test.gr2", "a.gr2"))
    (src / "monster" / "a" / "a.gr2").write_bytes(synthetic_gr2())
    (src / "monster" / "b" / "a.gr2").write_bytes(synthetic_gr2(sections=3))  # same name, other content
    (src / "monster" / "b" / "copy.gr2").write_bytes(synthetic_gr2())          # same content
    (src / "monster" / "b" / "x.data").write_bytes(b"MCOZ" + b"\0" * 20)
    s = build_manifest.build(src, tmp_path / "out")
    assert s["files"] == 5
    assert s["by_type"]["gr2"] == 3 and s["by_type"]["packed-encrypted(MCOZ)"] == 1
    rows = {r["source_relative_path"]: r for r in __import__("json").loads((tmp_path / "out" / "manifest.json").read_text())["files"]}
    assert rows["monster/b/copy.gr2"]["variant_of"].startswith("DUPLICATE_OF:")
    assert rows["monster/b/a.gr2"]["variant_of"].startswith("NAME_COLLISION:")
    msm = rows["monster/a/a.msm"]
    assert msm["status"] == "MISSING_DEPENDENCY" and "test.dds" in msm["missing"]
