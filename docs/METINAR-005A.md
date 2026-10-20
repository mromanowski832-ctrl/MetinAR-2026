# METINAR-005A — Prevent false aggro from GPS jitter

Issue: #12. Branch: `fix/12-gps-jitter-aggro`.

## Problem

`PrototypeMob` attacked whenever the raw estimated player→mob distance was `<= 12 m`, re-attacking every 8 s.
On the Galaxy A31 (METINAR-005, #6) the owner stood still ~13 m from the spider, and a GPS jump moved the
estimate onto the mob for ~40 s with 2.4–5.7 m reported accuracy, so the spider attacked over and over.

## Behavior

Aggro entry is decided by `AggroConfidenceGate` (`client/Assets/MetinAR/Scripts/WorldMap/AggroConfidenceGate.cs`),
a pure C# class with no UnityEngine dependency. `PrototypeMob` feeds it the latest `LocationFix`
(distance computed from that fix, its `HorizontalAccuracyMeters` and `TimestampSeconds`) every frame.

1. **Distinct fixes only.** The same fix timestamp is counted once, however many frames poll it.
2. **Accuracy gate.** A fix with an accuracy radius above `maxHorizontalAccuracyMeters` is ignored and breaks the streak.
3. **Confident proximity.** A fix only counts as inside when `distance + accuracy × accuracyMarginFactor <= aggroRange`,
   so the uncertainty circle lies inside the aggro range. Raw distance alone is never enough.
4. **Stability.** Aggro is entered when `requiredStableFixes` consecutive confident fixes span at least `minStableSeconds`.
   A gap above `maxFixGapSeconds` between fixes (e.g. pause/resume), or timestamps going backwards, resets the streak.
5. **Hysteresis / re-arm.** After an attack the mob is disengaged. It re-arms only after `requiredRearmFixes`
   consecutive accurate fixes with `distance − accuracy × accuracyMarginFactor >= aggroRange + rearmExtraMeters`.
   Oscillation around the 12 m boundary therefore cannot attack again.
6. **Minimum re-trigger time.** `minRetriggerSeconds` (fix time) between attacks even after re-arming; it replaces the old 8 s debounce.

The owner rule is unchanged: an aggressive mob still attacks automatically when the player is genuinely close.
"Genuinely close" now means confidently and stably close.

## Technical prototype thresholds (not game-design balance)

These are GPS noise-filtering values for the prototype. They are configurable per mob in the Inspector
(`PrototypeMob → Aggro Gate Settings`) and are **not** final combat or balance decisions.

| Setting | Value | Meaning |
|---|---|---|
| aggro range (existing, `PrototypeMob`) | 12 m | unchanged prototype value |
| `maxHorizontalAccuracyMeters` | 10 m | worse fixes are ignored |
| `accuracyMarginFactor` | 1.6 | platform accuracy is ~68% radius; ×1.6 ≈ 95% 2D radius |
| `requiredStableFixes` | 3 | consecutive confident fixes |
| `minStableSeconds` | 2 s | minimum span of the streak |
| `maxFixGapSeconds` | 10 s | larger gap resets history |
| `rearmExtraMeters` | 6 m | re-arm beyond 12 + 6 = 18 m (confidently) |
| `requiredRearmFixes` | 2 | consecutive far fixes to re-arm |
| `minRetriggerSeconds` | 8 s | minimum time between attacks |

Practical effect: with 2 m accuracy the player must stay within ~8.8 m; with 3 m within ~7.2 m, for about 2 s or longer.

## Verification

- Pure tests (`tests/GeoCore.Tests/AggroConfidenceGateTests.cs`, using the production source): stationary jitter around and
  outside the boundary, poor-accuracy fixes, single close fix, stable approach triggers once, boundary oscillation before and
  after a trigger, leave far and return re-arms, single far / inaccurate far fix does not re-arm, minimum re-trigger time,
  fix gap and backward timestamps, invalid input, and a replay of the recorded Galaxy A31 stationary start (relative metres only).
- Offline replay of the full METINAR-005 walk log (72 distinct logged fixes, relative values only): the old rule had 12 fixes inside 12 m;
  the gate triggers 0 times.
- Play Mode smoke section F: with good mock accuracy (2 m), a genuine approach does not attack on the first inside fix,
  attacks exactly once after the stable fixes, and does not repeat while staying inside.

## Known limitations

- Walking past the mob without pausing (as on the METINAR-005 return leg, closest ~5.4 m at 3.3 m accuracy for a single fix)
  no longer triggers; the player has to be confidently close for ~2 s. The thresholds above can be tuned.
- The replay uses the fixes printed by the diagnostic log, which may be sparser than the provider's real fix rate.
- A sustained GPS error with an optimistic reported accuracy can still produce a false attack; the gate reduces it to at most one
  attack until the player is confidently 18 m away.
- The mock provider only emits a fix when it moves; the Editor path needs fresh fixes to build the streak.
