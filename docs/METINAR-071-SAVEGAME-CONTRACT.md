# METINAR-071: SaveGame contract v1 (Stage I persistent save), with the server side

Status: **prototype contract, local development only.** Issue: #71 (METINAR-035 STAGE I, §14). Refs #39 (identity), #56 (S11 server rewards).
Code: [`server/`](../server/README.md). Machine-readable contract: [`contracts/savegame.v1.schema.json`](contracts/savegame.v1.schema.json)
(JSON Schema 2020-12). Examples: [`contracts/examples/`](contracts/examples/).

This PR changes only `server/` and `docs/`. There is **no Unity client code**. The client half of #71 (writing to
`persistentDataPath`, `LocalPlayerSession`, the editor test, the A31 check) belongs to **MetinAR Developer**; see §10.
The server is **localhost only, with no authentication**. That gap is documented in §9 and belongs to #39/#56.

Anything marked **PLACEHOLDER** below is a technical stand-in so the contract stays complete. It is **not a design
decision**. The questions listed in §11 were settled by the production lead on 2026-10-23 (not by the owner); one of
them (§11.12) still waits for the owner's consent to a `.github/` change.

## 1. Principles

1. **One document, two modes.** The same SaveGame v1 JSON is written by the client to disk and served by the
   server. `mode` says who vouches for it:
   - `local`: written by the device without server confirmation. **Unverified.** The UI must show "tryb lokalny"
     (local mode) and must never present it as online progress. `revision` is always `0`.
   - `server`: exactly this state was confirmed by the server at `revision`.
2. **The server is authoritative and client claims are proposals.** The server never trusts a client value for
   progression (level/EXP), currency, item contents, rewards or history. A client can only:
   - change client-owned fields (placement, loadout, current HP/MP, UI flags) through optimistic-concurrency sync;
   - **propose** an encounter reward, which the server validates, applies once, and records.
3. **Future sync needs no rewrite.** Local mode, server mode and the reward ledger all use the same document and the
   same `encounterId` idempotency key. Turning on real server rewards (#56) or local→server import (#39) changes server
   rules, not the format.
4. **Never silently reset.** A corrupt, unreadable or too-new save is kept, reported and never overwritten by a fresh profile.

## 2. Document (summary; the schema is normative)

| Field | Owner | Notes |
|---|---|---|
| `schemaVersion` | contract | `1`. See §4. |
| `mode` | server/client | `local` \| `server` (§1). |
| `playerId` | server | Must equal the route id. Prototype ids are unauthenticated (#39). |
| `revision` | server | `0` = never written (local mode, or the server's virtual initial save); the server adds 1 on every write. It is the ETag. |
| `updatedAtUtc` | writer | UTC `yyyy-MM-ddTHH:mm:ss.fffZ`. The server overwrites it on write. |
| `profile.displayName` | client | 1–32 characters, trimmed, no control characters. Mirrors `player_profiles.display_name`. |
| `profile.createdAtUtc` | server | |
| `character.classId`, `pathId` | server | `warrior\|ninja\|shaman\|slajter`. Default class `warrior` with `starter_sword` until class choice exists (§11.7). |
| `character.totalExp`, `level` | server | `level` is **derived** from `totalExp` via [`design/tables/exp_curve.csv`](design/tables/exp_curve.csv) (embedded in the server). A mismatched pair is invalid. EXP is capped at the level-99 threshold. |
| `character.hpCurrent`, `mpCurrent` | client | Persisted across sessions. The server clamps them to 0…max HP/MP for the level and class on every write (§11.6), so a too-high value is stored as the max, not rejected. |
| `stats.*` | server | Allocated primary stats and free points. Each level-up grants **2** free stat points (§11.3). There is no allocation endpoint yet (S6), so the client cannot spend them today. |
| `skills.freeSkillPoints`, `skills.progress[]` | server | Rank model per `classes_skills.json` (PROPOSAL #40). Each level-up grants **1** free skill point (§11.3). |
| `skills.loadout` | client | Attack/aura/slot1/slot2 skill ids. |
| `inventory.capacity` | server | `24`, `32`, `40`, `48` or `56`: 24 at start, Sakwa +8, max 56 ([ITEMS_COMBAT_SPEC §5](design/ITEMS_COMBAT_SPEC.md#L113), Q17). |
| `inventory.stacks[]`, `items[]` | client places / server owns contents | Unique `slotIndex` < capacity. `definitionId` must be in the item catalog (below). A stack must be a stackable item, with quantity 1…its stack limit (e.g. 50 consumables, 99 materials, 20 smith oil). An item instance must be a non-stacking item. Materials are ordinary stacks here (§11.4). |
| `inventory.lootStash` | server adds, client may take out | Loot that did not fit the bag: **at most 8 entries** (stacks + items together), each with `expiresAtUtc` = grant time + **24 h**. Expired entries are **lost** (§11.7). A client may move whole entries into the bag but never add or change one (`409`, field `inventory.lootStash`). |
| `equipment[]` | client places / server owns contents | Unique `slot`. |
| item instance `instanceId` | server | Lowercase UUID, unique across bag, stash and equipment. Moving an item is allowed; changing its `definitionId`/level/rarity/temper/affixes is not. `locked` is client-owned. |
| `currency.talers` | server | |
| `quests[]` | server | **PLACEHOLDER** shape (id, status, ≤16 counters), outside Stage I (§11.5). |
| `exploration` | server | Meters credited, a daily bucket, discovered cell ids. Cell id scheme is a **PLACEHOLDER**, outside Stage I (§11.5). |
| `encounterResults[]` | server (local mode: client) | Reward history and idempotency ledger mirror, ≤200 newest (§6). |

**Item catalog.** The server embeds `docs/design/tables/items_starter.csv`, `shop_starter_merchant.csv` and
`skill_tier_items.csv` (expanding `<classId>`), plus `rarity_tiers.csv` and `temper_levels.csv` for item bonuses to max
HP/MP. The tables README calls their column format a draft ([tables/README.md](design/tables/README.md#L4)); a column
rename breaks the server build's tests, which is intended.

"Owner" means who may change the value. The server ignores nothing silently: a sync that changes a server-owned
value is rejected with `409` and the list of fields (§5.2).

## 3. Local file format (client, for MetinAR Developer)

The file lives in `Application.persistentDataPath`:

```
savegame.msave        current save
savegame.msave.bak    previous good save
savegame.msave.tmp    write in progress (never trusted)
```

Per [MODES_DATA_MODEL §3](design/MODES_DATA_MODEL.md), the offline profile is local JSON marked unverified. The content is an ASCII header line followed by the UTF-8 JSON document:

```
METINAR-SAVE 1 sha256=<64 lowercase hex of the JSON bytes> bytes=<JSON byte length>\n
{ ...SaveGame v1 JSON... }
```

See [`examples/savegame.v1.local.example.msave`](contracts/examples/savegame.v1.local.example.msave). The server test
suite checks that its header matches the body.

### 3.1 Atomic write

1. Serialize, compute SHA-256 and the length, and write the header and body to `savegame.msave.tmp`.
2. `FileStream.Flush(flushToDisk: true)`, then close.
3. If `savegame.msave` exists: `File.Replace(tmp, current, bak)`. This atomically makes tmp current and the old
   current `.bak`. Otherwise use `File.Move(tmp, current)`.
4. Only after step 3 succeeds is the change "saved". On failure, keep the in-memory state and retry on the next trigger.

When to write: after every applied reward, after an equipment/inventory change, and on `OnApplicationPause(true)` / quit.
Debounce writes to at most one per second, except reward writes, which are always immediate.

### 3.2 Load and recovery

1. Read `savegame.msave`. It is valid when the header parses, `bytes` matches, the SHA-256 matches, the JSON parses,
   `schemaVersion` is supported (after migration, §4) and validation passes.
2. If it is invalid or missing, try `savegame.msave.bak` with the same checks. On success, restore it: keep the broken
   file renamed `savegame.msave.corrupt-<yyyyMMddTHHmmssZ>` and copy `.bak` to current.
3. A leftover `.tmp` means an interrupted write: **never load it**. Keep it as `.corrupt-…` for diagnostics.
4. If neither file is valid, show an explicit error ("Zapis uszkodzony"), keep all files, and offer an explicit
   "start a new profile" choice. **Never reset silently.**
5. If `schemaVersion` is newer than the build supports, refuse to load **and do not overwrite** (that would downgrade data).
   Tell the player to update the app.
6. With no files at all (first run), create the profile from defaults with `mode: "local"`, `revision: 0`.

## 4. Versioning and migrations

- `schemaVersion` is an integer and the document's first field. v1 is the first contract version.
- Migrations are **forward-only pure steps** vN → vN+1 on the raw JSON, applied in order before typed parsing
  (server: `SaveGameMigrator`; the client should mirror it). Each step sets `schemaVersion`.
- A document newer than the reader's version is refused (server `422 SAVEGAME_SCHEMA_UNSUPPORTED` with
  `receivedSchemaVersion` and `supportedSchemaVersion`; client: §3.2 step 5). A version older than the oldest
  readable one is refused the same way.
- Additive optional fields still need a version bump, because readers use strict parsing (unknown fields are rejected).
- Server database: migration `0004_savegame_and_reward_ledger.sql` adds `player_savegames`,
  `player_savegame_revisions` (history) and `encounter_rewards` (ledger). Existing players get a virtual initial save
  derived from `player_profiles` (EXP → level via the curve, current HP/MP) until the first write. Nothing is backfilled or deleted.

## 5. Server API (additive under `/api/v1`, D3 rule)

All errors are RFC 9457 `application/problem+json` with `code` (see [METINAR-010 §7](METINAR-010-BACKEND.md#7-error-format)).

### 5.1 `GET /api/v1/player/{playerId}/savegame`

`200` `{ "revision": n, "saveGame": {…}, "serverTime": "…" }` plus `ETag: "n"`. A player without a stored save gets a
deterministic initial save at revision `0` (the starter weapon has a UUID derived from the player id, so repeated GETs
are identical). Errors: `400 VALIDATION_FAILED`, `404 PLAYER_NOT_FOUND`, `500 SAVEGAME_INTEGRITY_FAILED`.

### 5.2 `PUT /api/v1/player/{playerId}/savegame` (sync of client-owned fields)

Requires `If-Match: "<revision>"` (strong ETag from the last GET/PUT/reward). The body is a full SaveGame with
`mode: "server"` and `revision` equal to the If-Match value. The server:

1. Checks the size (`SaveGameLimits:MaxBodyBytes`, default 1 MiB) → `413 PAYLOAD_TOO_LARGE`.
2. Parses the JSON → `400 MALFORMED_REQUEST` (bad JSON, not an object, unknown field, missing field, wrong type).
3. Checks the version and migrates → `422 SAVEGAME_SCHEMA_UNSUPPORTED`.
4. Validates every rule in the schema plus cross-field rules (unique slots/ids, level = curve(totalExp), `local-` ids
   only in local mode, catalog membership, per-item stack limits, capacity 24–56 in steps of 8, ≤8 stash entries)
   → `400 VALIDATION_FAILED` with `errors`.
5. `mode: "local"` → `422 SAVEGAME_LOCAL_MODE_NOT_SYNCABLE` (local import is #39, an owner decision).
6. Compares with the stored revision → `412 SAVEGAME_REVISION_CONFLICT` with `currentRevision`. A missing If-Match
   → `428 SAVEGAME_PRECONDITION_REQUIRED`.
7. Drops expired stash entries from both the stored and the incoming document (same server time), then compares
   server-owned values → `409 SAVEGAME_SERVER_OWNED_FIELD_CHANGED` with `fields`, e.g.
   `["character.totalExp","currency","items","inventory.lootStash"]`.
8. Clamps `hpCurrent`/`mpCurrent` to the max (§11.6), then stores it with compare-and-set in one SQLite
   `BEGIN IMMEDIATE` transaction, sets `mode: "server"`, revision+1 and
   `updatedAtUtc`, mirrors display name/level/EXP into `player_profiles` and appends to `player_savegame_revisions`.
   Returns `200` like GET, with the new ETag.

Concurrent PUTs on the same revision: exactly one wins and the rest get `412` (tested with 8 in parallel).

### 5.3 `POST /api/v1/encounters/reward` (reward proposal, idempotent by `encounterId`)

```json
{ "encounterId": "…", "playerId": "…", "nonce": "<from start>",
  "claim": { "exp": 30, "talers": 12,
             "stacks":    [ { "definitionId": "potion_hp_s", "quantity": 2 } ],
             "items":     [ { "definitionId": "starter_boots", "itemLevel": 1, "rarity": "common" } ],
             "materials": [ { "definitionId": "ore_shard", "quantity": 1 } ] } }
```

Checks run in this order: validation `400` → `404 ENCOUNTER_NOT_FOUND` → `403 ENCOUNTER_PLAYER_MISMATCH` → `403 ENCOUNTER_NONCE_MISMATCH`
→ `410 ENCOUNTER_EXPIRED` → `409 ENCOUNTER_NOT_COMPLETED` (the reward is tied to the server-side
`POST /encounters/complete`) → `422 REWARD_CLAIM_REJECTED` (above the prototype caps in `SaveGameLimits`; **not
recorded**, so a corrected claim can still apply).

Then, in **one `BEGIN IMMEDIATE` transaction**: if `encounter_rewards` already has the encounter, return the stored
result unchanged. Otherwise apply the claim, write the save (revision+1, history reason `encounter_reward`) and insert the
ledger row:
- EXP is added and capped, and the level is re-derived.
- Talers are added (overflow-checked).
- Each level gained adds 2 free stat points and 1 free skill point (§11.3).
- Stacks, then materials (both are inventory stacks, §11.4): top up bag stacks of the same item to its stack limit,
  then fill the lowest free slots in stacks of at most the limit, then go to the loot stash.
- Items get server-minted UUIDs and go to the lowest free slot, else the loot stash.
- A new stash entry expires 24 h after the grant. Expired entries are removed first.
- HP/MP are clamped to the new max (a level-up does not heal).
- An `encounterResults` entry (`source: server`, `status: applied`) is appended.

If something must go to the stash and its 8 places are full: `409 REWARD_NO_SPACE`, nothing is recorded, and the client
should free space and retry. A claim with only EXP/talers always fits.

Claim checks (`400 VALIDATION_FAILED`): every `definitionId` must be in the item catalog; `stacks` must be stackable
non-materials, `materials` must be catalog materials, `items` must be non-stacking items. A quantity above an item's
stack limit is allowed and split into several stacks (the prototype cap `MaxRewardQuantityPerEntry` = 99 still applies).

`200` `{ applied, alreadyApplied, claimMatchesApplied, granted, revision, saveGame }` plus `ETag`. A replay (lost
response, retry, double tap, parallel requests) returns `alreadyApplied: true` and the **original** `granted`.
`claimMatchesApplied: false` flags a retry that proposed something different, which is useful for client bug detection.

> **The claim is a proposal, not truth.** Today the server checks ownership, nonce, completion, expiry, caps,
> format and that each `definitionId` exists in the catalog, but **not** whether the values match a drop table and the
> mob level. Computing the reward on the server is S11 (#56, §11.2). The endpoint contract is designed so #56 can
> replace "apply the claim" with "compute the reward" without a client change.

### 5.4 Server-mode client flow

1. On start: `GET savegame` and keep the ETag. The client may cache the response for offline display. Progress made
   offline is **not imported** into the server save in v1 (§11.1): offline rewards stay `pending` and nothing is
   applied locally as if it were confirmed.
2. On mob kill: `POST /encounters/complete`, then `POST /encounters/reward`. Before sending, append the encounter
   locally as `status: "pending"`. Replace the document with the response `saveGame`.
3. On network failure: retry the same reward request (it is idempotent). After a restart, retry every `pending`
   entry first, then `GET`.
4. Placement/loadout change: `PUT` with the ETag. On `412`, GET, re-apply the client-owned change and retry. Flush
   pending rewards before a PUT.

## 6. Reward idempotency (both modes)

- The key is `encounterId`. On the server it is the encounter session id (32 hex characters) and the `encounter_rewards` primary key.
- In local mode the client generates `local-<random hex>` ids and records the result as `source: "local"`,
  `status: "local_unverified"` **in the same save write** that applies the reward, so a crash cannot apply it twice.
  Before applying, check that no entry with that `encounterId` exists.
- The server rejects `local-` ids and `local_unverified` entries in a server-mode document (`400`).
- `encounterResults` keeps the newest 200 entries. The server ledger itself is never trimmed. Retention of 200 is settled (§11.9).

## 7. Integrity on the server

- Each stored payload has `payload_sha256`. On read the server verifies the checksum, parses, migrates and checks that
  `playerId` and `revision` match the row. Any mismatch → `500 SAVEGAME_INTEGRITY_FAILED`, an error log entry (event
  110, player id only), and **no write**: PUT and reward are refused, so the corrupt row is kept for inspection and never reset.
- Writes are atomic SQLite transactions (WAL), and the previous revisions stay in `player_savegame_revisions`.

## 8. Validation implementation note

The server validates in code (`SaveGameValidator` plus strict System.Text.Json: unknown members disallowed,
`required` members) rather than with a JSON Schema library. No such library was added: the usual .NET choice (JsonSchema.Net) has
changed its licensing terms in recent versions, and adding it needs an owner licence review. Tests keep the schema, examples and DTOs in step:
- the examples deserialize strictly, validate and round-trip;
- top-level fields, `schemaVersion` and enums match.

Kept in code by decision (§11.11).

The examples were also checked against the schema with Python `jsonschema` in a throw-away venv. That tool is not part of the repo.

## 9. Security gap (prototype, known)

- No authentication: anyone reaching the port can read and write any `playerId` save and claim rewards for
  encounters they started. This is acceptable **only** because the server binds to `localhost` (`AllowedHosts` and `Urls`
  unchanged). Identity, tokens and HTTPS are **#39**; hosting is out of scope.
- The reward claim is client-proposed (§5.3). Caps limit damage, but they are not a cheat defence.
- Logs carry ids and revisions only, never payloads or coordinates.

## 10. Developer handoff (MetinAR Developer, Unity client)

What the client needs to do for #71 acceptance (the server side is ready to be used later, but Stage I can be **local only**):

1. Add a `SaveGame` model mirroring `savegame.v1.schema.json`, with strict parsing that rejects unknown fields.
2. `LocalPlayerSession`: load at start per §3.2, create a local-mode profile if no file exists, and save per §3.1 on
   reward, equipment change and pause/quit.
3. Show "tryb lokalny" whenever `mode == "local"`. Never label it online.
4. Spider kill reward: apply level/EXP/talers via `exp_curve.csv` (same derivation as the server) and record
   `encounterResults` with a `local-` id in the same write.
5. Tests: .NET serialization round-trip using `contracts/examples/*.json`; a corrupted-file test (flip a byte, then
   `.bak` restore); an interrupted write (leftover `.tmp` ignored); too-new `schemaVersion` refused without overwrite;
   reward idempotency by `encounterId`. A31: kill the spider, restart the app, and level/EXP/talers are unchanged.
6. Do not call `PUT savegame` with a local save. It is refused by design; v1 has no local→server import (§11.1).
7. Mirror the server rules in local mode: bag capacity 24 (+8 per Sakwa, max 56); stack limits from the item tables;
   materials as ordinary bag stacks; overflow to an 8-entry loot stash with `expiresAtUtc` = +24 h, and expired
   entries removed on load; +2 stat and +1 skill point per level-up; HP/MP clamped to the class max
   (CHARACTER_PROGRESSION §1.2/§1.3).

## 11. Settled questions

All twelve questions from the first version of this contract were settled by the **production lead** on 2026-10-23.
They are **not owner decisions**. Each entry gives the source and the spec lines it relies on. Line links point to
`docs/design/` as of this branch.

1. **Local → server import.** **ROZSTRZYGNIĘTE** (decyzja kierownika produkcji 2026-10-23 04:31): no import of local progress in v1. Local mode is
   explicit and unverified; the server is authoritative (#71 / METINAR-035 §14;
   [MODES_DATA_MODEL.md#L57](design/MODES_DATA_MODEL.md#L57) to L58). Implemented: `PUT` refuses `mode: "local"` (`422`).
2. **Server-computed rewards.** **ROZSTRZYGNIĘTE** (decyzja kierownika produkcji 2026-10-23 04:31): the server will compute rewards from drop tables and
   mob level, and validate `definitionId` against the catalog; that implementation is **#56** (S11,
   [OPEN_QUESTIONS_PLAN.md#L57](design/OPEN_QUESTIONS_PLAN.md#L57); [MODES_DATA_MODEL.md#L67](design/MODES_DATA_MODEL.md#L67) to L68).
   The claim fields stay in the contract. Drop tables are **not** implemented here. The catalog check on claims is already enforced (§5.3).
3. **Level-up grants.** **ROZSTRZYGNIĘTE** (decyzja kierownika produkcji 2026-10-23 04:31): per CHARACTER_PROGRESSION, computed by the server: **2** free
   stat points and the class's automatic +1 in two stats per level-up ([CHARACTER_PROGRESSION.md#L23](design/CHARACTER_PROGRESSION.md#L23),
   class table [L25–L30](design/CHARACTER_PROGRESSION.md#L25)), and **1** skill point per level-up from level 2
   ([CLASSES_SKILLS.md#L26](design/CLASSES_SKILLS.md#L26)). Implemented. Gaps: no allocation endpoint (S6); death EXP
   loss ([CHARACTER_PROGRESSION.md#L113](design/CHARACTER_PROGRESSION.md#L113)) is not implemented.
4. **Materials.** **ROZSTRZYGNIĘTE** (decyzja kierownika produkcji 2026-10-23 04:31): there is no separate bag in the player model
   ([MODES_DATA_MODEL.md#L48](design/MODES_DATA_MODEL.md#L48) to L51), so materials are inventory stacks (bag 24 → 56,
   [ITEMS_COMBAT_SPEC.md#L113](design/ITEMS_COMBAT_SPEC.md#L113) to L115; Sakwa [ECONOMY.md#L38](design/ECONOMY.md#L38)) with the stack
   limit from §2 ([ITEMS_COMBAT_SPEC.md#L56](design/ITEMS_COMBAT_SPEC.md#L56): 99, oil 20). Implemented: the top-level
   `materials[]` field is **removed** from the document. `claim.materials` / `granted.materials` stay as a reward category.
5. **Quests and exploration.** **ROZSTRZYGNIĘTE** (decyzja kierownika produkcji 2026-10-23 04:31): stay **PLACEHOLDER** shapes, versioned by
   `schemaVersion`, outside Stage I (S10 [OPEN_QUESTIONS_PLAN.md#L56](design/OPEN_QUESTIONS_PLAN.md#L56)). Documented only.
6. **HP/MP.** **ROZSTRZYGNIĘTE** (decyzja kierownika produkcji 2026-10-23 04:31): clamped to the max from level and class and persisted across sessions.
   Max HP = `60 + L × hpPerLevel + Vigor × 6 + items`, max MP = `20 + L × mpPerLevel + Spirit × 5 + items`
   ([CHARACTER_PROGRESSION.md#L40](design/CHARACTER_PROGRESSION.md#L40) to L41; Vigor/Spirit = start + auto growth + allocated).
   Item part: `max_hp`/`max_mp` base stats × rarity multiplier ([ITEMS_COMBAT_SPEC.md#L69](design/ITEMS_COMBAT_SPEC.md#L69) to L73) ×
   (1 + temper %) ([ITEMS_COMBAT_SPEC.md#L84](design/ITEMS_COMBAT_SPEC.md#L84)) + `max_hp_flat`/`max_mp_flat` affixes, of
   **equipped** items, rounded up. Implemented on `PUT`, reward and the initial save. The spec labels the formula "propozycja"
   and does not define rounding; the server rounds the item part up.
7. **Loot stash and defaults.** **ROZSTRZYGNIĘTE** (decyzja kierownika produkcji 2026-10-23 04:31): **8** places, **24 h**, expired loot is **lost**;
   bag capacity cap **56** ([ITEMS_COMBAT_SPEC.md#L114](design/ITEMS_COMBAT_SPEC.md#L114) to L116). Default class `warrior` with `starter_sword`.
   Implemented and tested, including `409 REWARD_NO_SPACE`. Note: the spec line only says "8 miejsc, 24 h, żeby nic nie przepadło";
   that expired loot is lost comes from this decision. Expiry is applied on read (`GET` view) and stored on the next write.
8. **Stack size limits.** **ROZSTRZYGNIĘTE** (decyzja kierownika produkcji 2026-10-23 04:31): limits from the item tables (50/99, also 20/10/5 for tier items)
   are enforced by the server ([ITEMS_COMBAT_SPEC.md#L52](design/ITEMS_COMBAT_SPEC.md#L52) to L58; column `stack` in
   [tables/items_starter.csv](design/tables/items_starter.csv), [shop_starter_merchant.csv](design/tables/shop_starter_merchant.csv),
   [skill_tier_items.csv](design/tables/skill_tier_items.csv)). Implemented and tested.
9. **History retention.** **ROZSTRZYGNIĘTE** (decyzja kierownika produkcji 2026-10-23 04:31): keep 200 newest `encounterResults` in the document; the ledger and
   `player_savegame_revisions` are not trimmed. Unchanged.
10. **HTTPS / auth / hosting.** **ROZSTRZYGNIĘTE** (decyzja kierownika produkcji 2026-10-23 04:31): Stage V (#39/#56); localhost only until then (§9). Documented
    only. "Stage V" is the coordinator's stage numbering; the repo plan has no stage of that name.
11. **JSON Schema library.** **ROZSTRZYGNIĘTE** (decyzja kierownika produkcji 2026-10-23 04:31): validation stays in code (§8), no new dependency. Unchanged.
12. **CI path filter.** **zatwierdzone przez kierownika, czeka na zgodę właściciela na zmianę .github/** (decyzja kierownika produkcji 2026-10-23 04:31):
    add `docs/design/tables/**` and `docs/contracts/**` to the `push` and `pull_request` paths of
    `.github/workflows/server-tests.yml`. **Not changed in this PR.** Until then, a change that touches only those
    folders does not run server CI. That matters more now, because the server embeds five more tables.

## 12. Verification

Gates run in `server/`:
- `dotnet build -c Release` (warnings as errors);
- `dotnet format --verify-no-changes`;
- `dotnet test` (150 tests);
- `scripts/smoke-local.sh` (real process: savegame, reward, replay, 428/412, restart persistence);
- `dotnet list package --vulnerable --include-transitive`.
