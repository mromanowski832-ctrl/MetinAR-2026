# METINAR-004 — First visible world-map HUD prototype

## Scope

Prototype-only **world map + phone HUD** scene for MetinAR. Original placeholder art only
(no Metin2 assets). No map SDK, backend, login, real inventory/skills/stats, hand tracking,
sword combat, AR fight flow, drops, or monetization.

Branch: `feat/004-world-map-hud`. Scene:
`client/Assets/MetinAR/Scenes/WorldMapPrototype.unity`.

## What you see

- Local meter world anchored at mock GPS start (Null Island by default): +Z north, +X east.
- Player capsule placeholder driven by `PlayerLocationService` + `MockLocationProvider`.
- One aggressive mob (“Trujący Pająk”) and one stationary NPC.
- Portrait HUD (1080×1920 scaler): portrait/stats, radar placeholder, quest tracker,
  spinning coin + EXP segments, bottom buttons (EQ / Skille / Ustawienia / Postać / Menu (hamburger)).
- Panels: mob info, inventory grid, character, placeholder menus.

## Mock movement (Editor / Development Build)

`EditorMockMovement` (compiled out of release player builds without `DEVELOPMENT_BUILD`):

| Input | Action |
|-------|--------|
| W / ↑ | Move forward (along heading) via `MockProvider.MoveMeters` |
| S / ↓ | Move backward |
| A / ← | Strafe left |
| D / → | Strafe right |
| Q / E | Rotate heading |

Location subsystem is unchanged; `GeoCoordinate` / `GeoMath` were not modified.

## Mob / NPC behaviour

- **Mob:** world name+distance label; click opens `MobInfoPanel`; if aggressive and player
  enters `aggroRangeMeters`, shows “Zostałeś zaatakowany” and logs
  `AR ENCOUNTER REQUESTED` (debounced). Panel button “Wejdź do AR” does the same log/toast.
- **NPC:** distinct color/shape, stationary, no gameplay logic.

## Not included

Real map tiles/SDK, networking, auth, final RPG systems, AR encounter gameplay, loot,
payments, hand tracking, weapons.

## Rebuild scene / screenshots

```text
Unity -batchmode -nographics -quit -projectPath client
  -executeMethod MetinAR.Editor.WorldMapPrototypeSceneBuilder.BuildScene

Unity -batchmode -nographics -quit -projectPath client
  -executeMethod MetinAR.Editor.CaptureWorldMapPrototypeScreenshots.Capture
```
