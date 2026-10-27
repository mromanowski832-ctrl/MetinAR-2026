#!/usr/bin/env python3
"""Validate real serialized catalog references and the pinned asset import; no Unity or pip packages.

Use --metadata-only only for a sparse local snapshot. CI always checks every imported byte.
This does not establish Unity compilation, animation playback, Android correctness or phone performance.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import re
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / "client/Assets/MetinAR"


def validate_prefab_dependencies(roots, guids, require):
    """Walk production prefab/controller dependencies. Imported FBX subasset IDs still need Unity.

    Material references are resolved, but package-owned shader GUIDs are outside this catalog's scope.
    """
    pending, visited = list(roots), set()
    builtin_guids = {"0000000000000000e000000000000000", "0000000000000000f000000000000000"}
    while pending:
        path = pending.pop()
        if path in visited:
            continue
        visited.add(path)
        text = path.read_text()
        for file_id, guid in re.findall(r"\{fileID: (-?\d+), guid: ([a-f0-9]{32}), type: \d+\}", text):
            if guid in builtin_guids:
                continue
            target = guids.get(guid)
            require(target is not None and target.is_file(), f"unresolved dependency in {path}: {guid}")
            if target.suffix in (".prefab", ".controller", ".mat", ".asset"):
                # Unity's virtual main Prefab ID is not a YAML object declaration.
                virtual_prefab = target.suffix == ".prefab" and file_id == "100100000"
                declared = re.search(r"^--- !u!\d+ &" + re.escape(file_id) + r"(?: stripped)?$",
                                     target.read_text(), re.M)
                require(virtual_prefab or declared is not None,
                        f"unresolved fileID {file_id} in {path} -> {target}")
            if target.suffix in (".prefab", ".controller"):
                pending.append(target)
    return visited


def validate(metadata_only=False):
    checks = 0

    def require(condition, message):
        nonlocal checks
        checks += 1
        if not condition:
            raise ValueError(message)

    manifest = json.loads((ROOT / "tools/assets/presentation-imports.json").read_text())
    require(len(manifest["imports"]) > 0, "empty import manifest")
    paths = set()
    for entry in manifest["imports"]:
        path = ROOT / entry["path"]
        require(entry["path"] not in paths, "duplicate imported path: " + entry["path"])
        paths.add(entry["path"])
        require(entry["source_pr"] in (36, 45, 63, 69), "unapproved asset source in integration")
        if not metadata_only:
            require(path.is_file(), "missing imported file: " + entry["path"])
            content = path.read_bytes()
            sha = hashlib.sha1(b"blob " + str(len(content)).encode() + b"\0" + content).hexdigest()
            require(sha == entry["git_blob_sha"], "import was modified: " + entry["path"])

    guids = {}
    for meta in ART.rglob("*.meta"):
        match = re.search(r"^guid: ([a-f0-9]{32})$", meta.read_text(), re.M)
        require(match is not None, "missing GUID: " + str(meta.relative_to(ROOT)))
        require(match[1] not in guids, "duplicate GUID: " + str(meta.relative_to(ROOT)))
        guids[match[1]] = Path(str(meta)[:-5])

    catalog_path = ART / "Resources/MetinARPresentation.asset"
    catalog = catalog_path.read_text()
    script = re.search(r"m_Script: \{fileID: 11500000, guid: ([a-f0-9]{32}), type: 3\}", catalog)
    require(script is not None and guids.get(script[1]) == ART / "Scripts/Presentation/GameArtCatalog.cs",
            "catalog script reference does not resolve")

    def prefab_reference(text, field):
        match = re.search(re.escape(field) + r": \{fileID: (\d+), guid: ([a-f0-9]{32}), type: 3\}", text)
        require(match is not None, "missing prefab reference: " + field)
        path = guids.get(match[2])
        require(path is not None and path.suffix == ".prefab" and path.is_file(), "unresolved prefab: " + field)
        require(re.search(r"^--- !u!1 &" + match[1] + r"$", path.read_text(), re.M) is not None,
                "wrong GameObject fileID: " + field)
        return path

    arms = prefab_reference(catalog, "_fppArms")
    merchant = prefab_reference(catalog, "_wanderingMerchant")
    buckler = prefab_reference(catalog, "_starterBuckler")
    if not metadata_only:
        validate_prefab_dependencies((arms, merchant, buckler), guids, require)
    ids = set()
    entries = re.findall(r"^  - _definitionId: ([a-z0-9_]+)\n(.*?)(?=^  - _definitionId:|\Z)", catalog, re.M | re.S)
    require(len(entries) == 21, "catalog must have 21 items")
    for item_id, body in entries:
        require(item_id not in ids, "duplicate catalog item: " + item_id)
        ids.add(item_id)
        name = re.search(r'^    _displayName: (".*")$', body, re.M)
        require(name is not None and bool(json.loads(name[1]).strip()), "empty item name: " + item_id)
        icon = re.search(r"_icon: \{fileID: 21300000, guid: ([a-f0-9]{32}), type: 3\}", body)
        require(icon is not None and icon[1] in guids, "unresolved icon: " + item_id)
        image = guids[icon[1]]
        require(image.parent == ART / "Textures/ItemIcons", "icon outside item icon set: " + item_id)
        require(image.is_file(), "missing icon bytes: " + item_id)
        data = image.read_bytes()
        require(data[:8] == b"\x89PNG\r\n\x1a\n" and struct.unpack(">II", data[16:24]) == (256, 256)
                and data[25] == 6, "icon must be a 256px RGBA PNG: " + item_id)
        if item_id == "starter_sword":
            prefab_reference(body, "_modelPrefab")
        if item_id == "starter_buckler":
            require(prefab_reference(body, "_modelPrefab") == buckler, "buckler item/prefab mismatch")

    table_ids = set()
    classes = json.loads((ROOT / "docs/design/tables/classes_skills.json").read_text())["classes"]
    for filename in ("items_starter.csv", "shop_starter_merchant.csv", "skill_tier_items.csv"):
        with (ROOT / "docs/design/tables" / filename).open() as handle:
            for row in csv.DictReader(handle):
                item_id = row["itemId"]
                if "<classId>" in item_id:
                    table_ids.update(item_id.replace("<classId>", cls["id"]) for cls in classes)
                else:
                    table_ids.add(item_id)
    require(ids == table_ids, "catalog/table mismatch: " + str(ids ^ table_ids))
    for folder in ("Characters/Player/FppArms", "Characters/NPC/WanderingMerchant", "Weapons/FppSword",
                   "Weapons/StarterBuckler", "Textures/ItemIcons"):
        source = (ART / folder / "ASSET_SOURCE.txt").read_text()
        require(re.search(r"^origin:\s*original\s*$", source, re.M) is not None, "origin: " + folder)
        require(re.search(r"^license:\s*MetinAR-Owned\s*$", source, re.M) is not None, "license: " + folder)
        require(re.search(r"^commercial_use:\s*yes\s*$", source, re.M) is not None, "commercial_use: " + folder)
    return checks, len(manifest["imports"]), len(ids), len(guids)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--metadata-only", action="store_true", help="skip exact imported-byte checks in a sparse snapshot")
    args = parser.parse_args()
    try:
        checks, imports, items, guids = validate(args.metadata_only)
    except (ValueError, OSError, KeyError, IndexError, struct.error) as exception:
        print("[Presentation] FAIL:", exception, file=sys.stderr)
        return 1
    print(f"[Presentation] PASS checks={checks} items={items} guids={guids} imports={imports} "
          f"mode={'metadata-only' if args.metadata_only else 'full-byte-check'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
