# Characters/Mobs

Regular production mobs, one folder per mob id (PascalCase), e.g. `VenomousSpider/`.

```
<MobId>/
  ASSET_SOURCE.txt        provenance + licence (mandatory)
  Models/     SK_Mob_<MobId>.fbx  (meshes named *_LOD0/_LOD1/_LOD2 inside)
  Animations/ A_<MobId>_<State>[_NN].fbx, AC_<MobId>.controller
  Textures/   T_<MobId>[_Part]_<BC|N|MS|AO|E|M>.png
  Materials/  M_<MobId>[_Part].mat   (URP)
  Prefabs/    PF_Mob_<MobId>[_Variant].prefab
```

Budget (proposal): LOD0 <= 8k tris, <= 40 bones, <= 2 materials, 1K textures (2K only with justification).
Rules: docs/METINAR-009-ASSET-PIPELINE.md. Validate: MetinAR > Asset Pipeline > Validate.
