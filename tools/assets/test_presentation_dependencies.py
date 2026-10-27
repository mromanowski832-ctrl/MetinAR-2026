"""Negative tests against the real production prefab graph; no copied prefab or animation implementation."""
from pathlib import Path
import re
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True
from check_presentation_integration import ART, validate_prefab_dependencies


def require(condition, message):
    if not condition:
        raise ValueError(message)


class PresentationDependencyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.guids = {}
        for meta in ART.rglob("*.meta"):
            match = re.search(r"^guid: ([a-f0-9]{32})$", meta.read_text(), re.M)
            if match:
                cls.guids[match[1]] = Path(str(meta)[:-5])
        cls.roots = [ART / path for path in (
            "Characters/Player/FppArms/Prefabs/PF_Player_FppArms.prefab",
            "Characters/NPC/WanderingMerchant/Prefabs/PF_Npc_WanderingMerchant.prefab",
            "Weapons/StarterBuckler/Prefabs/PF_Weapon_StarterBuckler.prefab")]

    def test_real_prefabs_reach_nested_sword_and_both_controllers(self):
        visited = validate_prefab_dependencies(self.roots, self.guids, require)
        names = {path.name for path in visited}
        self.assertTrue({"PF_Weapon_FppSword.prefab", "AC_FppArms.controller", "AC_WanderingMerchant.controller"} <= names)

    def test_missing_nested_sword_guid_is_rejected(self):
        guids = {guid: path for guid, path in self.guids.items() if path.name != "PF_Weapon_FppSword.prefab"}
        with self.assertRaisesRegex(ValueError, "unresolved dependency"):
            validate_prefab_dependencies(self.roots, guids, require)

    def test_registered_but_missing_fbx_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            guids = {guid: Path(folder) / "missing.fbx" if path.name == "A_FppArms_Attack.fbx" else path
                     for guid, path in self.guids.items()}
            with self.assertRaisesRegex(ValueError, "unresolved dependency"):
                validate_prefab_dependencies(self.roots, guids, require)

    def test_wrong_material_file_id_is_rejected(self):
        sword = ART / "Weapons/FppSword/Prefabs/PF_Weapon_FppSword.prefab"
        text = sword.read_text()
        broken = text.replace("fileID: 2100000,", "fileID: 2100001,")
        self.assertNotEqual(text, broken, "real sword prefab no longer references a material; update the fixture")
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / sword.name
            path.write_text(broken)
            with self.assertRaisesRegex(ValueError, "unresolved fileID 2100001"):
                validate_prefab_dependencies((path,), self.guids, require)


if __name__ == "__main__":
    unittest.main()
