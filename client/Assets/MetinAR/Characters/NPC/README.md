# Characters/NPC

NPCs, one folder per NPC id, e.g. `Blacksmith/` (Kowal). Humanoid rig.
Layout as Characters/Mobs with the `Npc` token: `SK_Npc_<Id>.fbx`, `PF_Npc_<Id>.prefab`,
`A_<Id>_<State>.fbx` (Idle, Walk, TurnLeft, TurnRight, Talk, Interact + NPC-specific e.g. Hammering),
`AM_<Id>_<Part>.mask`, `T_<Id>_<Role>.png`, `M_<Id>.mat`, `ASSET_SOURCE.txt`.

Held props/tools (the Blacksmith hammer) are separate objects under `Weapons/`.
Budget (proposal): LOD0 10-20k tris, <= 75 bones, <= 3 materials, 2K textures.
Rules: docs/METINAR-009-ASSET-PIPELINE.md.
