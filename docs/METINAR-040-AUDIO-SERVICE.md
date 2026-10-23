# METINAR-040 — AudioService and game event sounds (#85)

Stage II, P0. This PR adds a runtime sound service that plays the imported starter-kit SFX
([METINAR-033](METINAR-033-STARTER-KIT-SFX.md)) when game events happen. It also adds separate volumes for SFX, Music
and UI, a vibration switch and the licence gate. Event list: owner spec §13
([OWNER_SPEC L568-585 @ PR #96](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/2bb0a72eae0ab9cdb440b022d52927d30f358d1e/docs/design/OWNER_SPEC_2026-10-23.md#L568-L585))
and [COMBAT_PHYSICS §5](design/COMBAT_PHYSICS.md). Music tracks (#68) are **out of scope**. Only the Music mixer group,
its volume and `AudioService.PlayMusic/StopMusic` are ready.

**The Developer's combat files were not edited** (`FppCombatController`, `ArEncounterSceneController`,
`ArEncounterCombat`, `ArEncounterHud`, `EncounterCombatSession`). The bridge only subscribes to their public events
and reads the public FPP timeline. Sound moments that have no public event yet are listed below as one-line hooks.

## Architecture

| Layer | Files | Unity? |
|---|---|---|
| Core (asmdef `MetinAR.Audio`, `noEngineReferences`) | `Scripts/Audio/Core/`: `GameAudioEvent`, `AudioCue`, `AudioCueTable`, `AudioEventDirector`, `AudioSettingsModel`, `GameAudio` (bus), `CombatAudioRules`, `FppAudioObserver` | no (tested in .NET) |
| Runtime (Assembly-CSharp) | `Scripts/Audio/`: `AudioService`, `AudioGameEventBridge`, `AudioSettingsOverlay`, `UiClickSound`, `AudioLibrary`, `PlayerPrefsAudioSettingsStore`, `UnityHaptics` | yes |
| Data | `Resources/Audio/MetinARAudioLibrary.asset` (27 clip refs plus the mixer), `Audio/Mixers/MetinARMixer.mixer` (4 exposed params) | yes |
| Editor | `Editor/Audio/AudioLibraryBuilder.cs` (menu **MetinAR/Audio/Rebuild Audio Library**), `Editor/Audio/AudioServiceSmokeTest.cs` (menu **MetinAR/Audio/Run Audio Service Smoke**) | yes |

How it works:

- `AudioService` bootstraps itself (`RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`). It creates a
  DontDestroyOnLoad object `MetinARAudio` that holds the service, the bridge and the settings overlay.
- `GameAudio.Raise(GameAudioEvent)` is a static bus and the only call gameplay code needs.
- `AudioEventDirector` (pure C#) handles the logic:
  - **per-event anti-spam** (`AudioCue.MinIntervalSeconds`);
  - **variation**: a random variant, never the same one twice in a row, plus random pitch within the cue's range;
  - **muted channels are skipped** (no voice is used);
  - **haptics** fire only for haptic cues, only when the switch is on, and at most once every 0.25 s.
- Voices: 8 SFX and 3 UI `AudioSource`s on the SFX/UI mixer groups. When all voices are busy, the oldest is stolen.
  There is one Music source and one loop source per `GameAudioLoop` (Aura), each with a short fade.
- Volumes: `AudioSettingsModel` stores 0..1 values in PlayerPrefs under `metinar.audio.master/sfx/ui/music/haptics`
  (until the settings save from #71). It maps them to dB (0 → −80 dB, 1 → 0 dB, otherwise 20·log10) and writes the
  exposed params `MasterVolume`, `SfxVolume`, `UiVolume` and `MusicVolume`. If the mixer is missing, the service falls
  back to per-source gain. Defaults: SFX 100 %, UI 80 %, Music 70 %, vibration on.
- Pause and resume: `OnApplicationPause` (and focus loss on a device) sets `AudioListener.pause`, so all sources
  stop and resume together with no click. The Aura loop fades in and out over 0.15 s. Focus loss is ignored in the
  Editor.
- API: `AudioService.Instance.SetVolume(AudioChannel, float)`, `.SetHaptics(bool)`, `.Settings`, `.Play(event)`,
  `.PlayMusic(clip)` / `.StopMusic()`.

## Settings screen

`AudioSettingsOverlay` has its own overlay canvas, so no scene or HUD files were edited:

- A **„Dźwięk”** button in the top-right corner opens the panel. The panel also opens automatically when the world
  map's Settings placeholder panel opens.
- Panel contents: sliders **Efekty** (SFX), **Muzyka**, **Interfejs** (UI) showing percentages, a **„Wibracje:
  WŁ/WYŁ”** toggle and **„Zamknij”**.
- Changes apply immediately and are saved.

## Event → clip (paths relative to `client/Assets/MetinAR/Audio/`)

| Event | Source in game | Clip(s) | Group | Haptic |
|---|---|---|---|---|
| SwordSwing | FPP timeline `SwingId` changed (bridge) | `SFX/Combat/SFX_Combat_SwordSwing_01.wav` (pitch 0.95–1.05) | SFX | – |
| SwordHit | `ArEncounterCombat.HitResolved`, landed | `SFX/Combat/SFX_Combat_SwordHit_01.wav` | SFX | ✔ |
| SwordCrit | HitResolved, `IsCritical` | SwordHit, low pitch 0.82–0.86 *(placeholder)* | SFX | ✔ |
| SwordMiss | HitResolved, `Miss` | SwordSwing, quiet, pitch 1.18–1.24 *(placeholder whiff)* | SFX | – |
| ShieldBlock | HitResolved, `Blocked` / `Parried` | `SFX/Combat/SFX_Combat_ShieldBlock_01.wav` | SFX | ✔ |
| AuraStart / AuraStop (+ loop) | FPP timeline Aura cast / active edges | `SFX/Combat/SFX_Combat_AuraStart/AuraLoop/AuraStop_01.wav` | SFX | – |
| MobAppear | `ArEncounterSceneController.MobPlaced` | `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_01/02/03.wav` | SFX | – |
| MobAttack + PlayerHurt | `WorldMapPrototypeController.PlayerAttacked` | `…VenomousSpider_Attack_01.wav`; PlayerHurt = SwordHit, pitch 0.68–0.72 *(placeholder)* | SFX | PlayerHurt ✔ |
| MobHurt | HitResolved, landed and not killed | `…VenomousSpider_Hit_01.wav` | SFX | – |
| MobDeath + RewardCoins | HitResolved, `Killed` | `…VenomousSpider_Death_01.wav` + `SFX/UI/SFX_UI_CoinPickup_01.wav` | SFX | – |
| LevelUp | HitResolved, `LevelsGained > 0` | `SFX/Stinger/SFX_Stinger_LevelUp_01.wav` | SFX | – |
| UiClick / UiDenied | every scene `Button` except `Action_*` (auto-attached `UiClickSound`) | `SFX/UI/SFX_UI_Click_01.wav` / `SFX_UI_Denied_01.wav` | UI | – |
| ArEnter | AR scene loaded | `SFX/UI/SFX_UI_ShopOpen_01.wav`, pitch 0.9 *(placeholder)* | UI | – |
| LootPickup, ItemEquip/Unequip Metal/Leather, PotionDrink, AntidoteDrink, PoisonTick | **hook needed** (below) | `SFX/Loot/SFX_Loot_Common_01.wav`, `SFX/Item/SFX_Item_*_01.wav`, `SFX/Status/SFX_Status_PoisonTick_01.wav` | SFX | – |
| ShopOpen, Purchase, Sell, UpgradeSuccess, UpgradeFail | **hook needed** (below) | `SFX/UI/SFX_UI_ShopOpen/Buy/Sell_01.wav`; Upgrade = Buy at pitch 1.1 / Denied at pitch 0.9 *(placeholders)* | UI | – |

The full table, including volumes, pitch and anti-spam intervals, is in `AudioCueTable.cs`. Each event plays
**exactly one** clip. No cue uses the Music group.

Placeholders that need dedicated clips later (follow-up for the audio team): crit, whiff, player hurt, upgrade
success/fail, AR enter, skill sounds other than Aura (no skill events exist yet).

## Hook points for the Developer (one line each)

These go in code the Developer owns or code that does not exist yet. Add the one line at the moment the
action is **confirmed**:

| Where (issue) | Line |
|---|---|
| AR mob attack on the player, the moment damage lands (#74 / #48) | `GameAudio.Raise(GameAudioEvent.MobAttack);` then `GameAudio.Raise(GameAudioEvent.PlayerHurt);` |
| Player shield blocks a mob attack (#94) | `GameAudio.Raise(GameAudioEvent.ShieldBlock);` |
| Loot picked up (#49) | `GameAudio.Raise(GameAudioEvent.LootPickup);` |
| Item equipped / unequipped (#49) | `GameAudio.Raise(GameAudioEvent.ItemEquipMetal);` (or `ItemUnequipMetal` / `ItemEquipLeather` / `ItemUnequipLeather`) |
| Shop window opened (#50) | `GameAudio.Raise(GameAudioEvent.ShopOpen);` |
| Purchase / sale confirmed, or refused for lack of gold (#50) | `GameAudio.Raise(GameAudioEvent.Purchase);` / `Sell` / `UiDenied` |
| Upgrade result (#54) | `GameAudio.Raise(GameAudioEvent.UpgradeSuccess);` / `UpgradeFail` |
| Potion / antidote drunk; poison damage tick | `GameAudio.Raise(GameAudioEvent.PotionDrink);` / `AntidoteDrink` / `PoisonTick` |
| (optional) skill or Aura cast without the FPP timeline | `GameAudio.Raise(GameAudioEvent.AuraStart);` + `GameAudio.SetLoop(GameAudioLoop.Aura, true/false);` |

`using MetinAR.Audio;` is all that is needed, because `MetinAR.Audio` is auto-referenced and has no engine
dependencies. Anti-spam and the volume and vibration switches apply automatically.

## Android manifest

`UnityHaptics` references `Handheld.Vibrate()` (the fallback after a 35 ms `Vibrator`/`VibrationEffect` pulse via
JNI). Unity therefore adds the normal-level **`android.permission.VIBRATE`**, which does not need a runtime prompt.
There is no other manifest change, and **ACCESS_BACKGROUND_LOCATION: ABSENT**.

## Verification

- `dotnet test tests/MetinAR.Audio.Tests/MetinAR.Audio.Tests.csproj` (CI `audio-core-tests`, also listed in
  `tools/qa/qa-config.json`) covers:
  - event → clip mapping, with exactly one clip per event;
  - anti-spam, variation and pitch;
  - volume 0 muting the group, including master;
  - **vibration off → `Vibrate()` never called**;
  - dB mapping, persistence and corrupt values;
  - combat rules (hit/crit/block/parry/miss/kill/level-up) and FPP edges;
  - the licence gate for every clip used (present in `LICENSES.md`, matching SHA-256, allowed licence, no NC/ND/SA,
    no forbidden sources);
  - library asset GUIDs vs clip `.meta`, and mixer exposed params bound to the right group volume.
- Unity Editor smoke (requires Unity 6000.6.4f1):

  ```
  Unity.exe -batchmode -nographics -projectPath client -executeMethod MetinAR.Editor.Audio.AudioServiceSmokeTest.Run -logFile - -quit
  ```

  It expects `[AudioServiceSmoke] PASS events=… clips=27`. It checks: library and mixer load; all 4 params readable;
  the SFX/UI/Music groups exist; every clip loads; each event plays exactly 1 clip on the right channel; haptics off
  means 0 vibrations.
- Play Mode: the Console shows `[Audio] play event=… clip=…` in Editor and development builds.

## Physical device (Samsung A31): UNVERIFIED (waiting for slot #33)

The A31 and LuzMen are reserved through #33, and the Developer holds the queue (#66). Steps once the slot is free:

1. Run the smoke command above.
2. Build the debug APK and install it.
3. Ask a human to confirm by ear:
   - swing, hit, block, Aura start/loop/stop;
   - the spider appearing, getting hurt and dying, plus coins;
   - UI clicks;
   - the three sliders, with volume 0 = silence;
   - vibration on hit, and none after switching it off;
   - no audio click on pause/resume (Home → back).
