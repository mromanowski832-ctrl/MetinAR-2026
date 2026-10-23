# METINAR-071: SaveGame contract v1 (Stage I persistent save), with the server side

Status: **prototype contract, local development only.** Issue: #71 (METINAR-035 STAGE I, §14). Refs #39 (identity), #56 (S11 server rewards).
Code: [`server/`](../server/README.md). Machine-readable contract: [`contracts/savegame.v1.schema.json`](contracts/savegame.v1.schema.json)
(JSON Schema 2020-12). Examples: [`contracts/examples/`](contracts/examples/).

This PR changes only `server/` and `docs/`. There is **no Unity client code**. The client half of #71 (writing to
`persistentDataPath`, `LocalPlayerSession`, the editor test, the A31 check) belongs to **MetinAR Developer**; see §10.
The server is **localhost only, with no authentication**. That gap is documented in §9 and belongs to #39/#56.

Anything marked **PLACEHOLDER** below is a technical stand-in so the contract stays complete. It is **not a design
decision**. The owner's open questions are in §11.

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
| `character.classId`, `pathId` | server | `warrior\|ninja\|shaman\|slajter`. Default class is a **PLACEHOLDER** (`warrior`). |
| `character.totalExp`, `level` | server | `level` is **derived** from `totalExp` via [`design/tables/exp_curve.csv`](design/tables/exp_curve.csv) (embedded in the server). A mismatched pair is invalid. EXP is capped at the level-99 threshold. |
| `character.hpCurrent`, `mpCurrent` | client | 0–999999. Bounds against max HP/MP are an open question. |
| `stats.*` | server | Allocated primary stats and free points. Level-up grants are not decided (S6). |
| `skills.freeSkillPoints`, `skills.progress[]` | server | Rank model per `classes_skills.json` (PROPOSAL #40). |
| `skills.loadout` | client | Attack/aura/slot1/slot2 skill ids. |
| `inventory.capacity` | server | Contract range 1–200. Default 24 per [ITEMS_COMBAT_SPEC §5](design/ITEMS_COMBAT_SPEC.md) / Q17 (proposal accepted 2026-10-21). The spec's maximum is 56 (Sakwa), and the server does not enforce it. |
| `inventory.stacks[]`, `items[]` | client places / server owns contents | Unique `slotIndex` < capacity. Quantity 1–999999 (stack size limits per item are not enforced; open question). |
| `inventory.lootStash` | server | Overflow when the bag is full. The spec says **8 slots, 24 h**. The contract and server allow ≤200 stacks + ≤200 items with **no expiry**: a **PLACEHOLDER**, open question §11 Q7. |
| `equipment[]` | client places / server owns contents | Unique `slot`. |
| item instance `instanceId` | server | Lowercase UUID, unique across bag, stash and equipment. Moving an item is allowed; changing its `definitionId`/level/rarity/temper/affixes is not. `locked` is client-owned. |
| `currency.talers` | server | |
| `materials[]` | server | One entry per definition. A separate bag vs. inventory stacks is an open question. |
| `quests[]` | server | **PLACEHOLDER** shape (id, status, ≤16 counters). The quest model is not decided. |
| `exploration` | server | Meters credited, a daily bucket, discovered cell ids. Cell id scheme is a **PLACEHOLDER**. |
| `encounterResults[]` | server (local mode: client) | Reward history and idempotency ledger mirror, ≤200 newest (§6). |

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
   only in local mode) → `400 VALIDATION_FAILED` with `errors`.
5. `mode: "local"` → `422 SAVEGAME_LOCAL_MODE_NOT_SYNCABLE` (local import is #39, an owner decision).
6. Compares with the stored revision → `412 SAVEGAME_REVISION_CONFLICT` with `currentRevision`. A missing If-Match
   → `428 SAVEGAME_PRECONDITION_REQUIRED`.
7. Compares server-owned values with the stored save → `409 SAVEGAME_SERVER_OWNED_FIELD_CHANGED` with
   `fields`, e.g. `["character.totalExp","currency","items"]`.
8. Stores it with compare-and-set in one SQLite `BEGIN IMMEDIATE` transaction, sets `mode: "server"`, revision+1 and
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
- Stacks merge into a matching bag stack, else take the lowest free slot, else go to the stash.
- Items get server-minted UUIDs and go to a free slot, else the stash.
- Materials merge.
- An `encounterResults` entry (`source: server`, `status: applied`) is appended.

If even the stash is full: `409 REWARD_NO_SPACE`, nothing is recorded, and the client should free space and retry.

`200` `{ applied, alreadyApplied, claimMatchesApplied, granted, revision, saveGame }` plus `ETag`. A replay (lost
response, retry, double tap, parallel requests) returns `alreadyApplied: true` and the **original** `granted`.
`claimMatchesApplied: false` flags a retry that proposed something different, which is useful for client bug detection.

> **The claim is a proposal, not truth.** Today the server checks ownership, nonce, completion, expiry, caps and
> format, but **not** whether the values match a drop table or whether a `definitionId` exists. That is S11 (#56). The
> endpoint contract is designed so #56 can replace "apply the claim" with "compute the reward" without a client change.

### 5.4 Server-mode client flow

1. On start: `GET savegame` and keep the ETag. The client may cache the response for offline display. What
   happens to progress made offline in server mode is **not decided** (§11 Q1). Until then, offline rewards stay
   `pending` and nothing is applied locally as if it were confirmed.
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
- `encounterResults` keeps the newest 200 entries. The server ledger itself is never trimmed. History retention is an open question.

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
6. Do not call `PUT savegame` with a local save. It is refused by design until #39 decides local import.

## 11. Open questions for the owner (not decided here)

1. **Local → server import** (#39): can local-mode progress ever be uploaded/merged, and how is it trusted?
2. **Server-computed rewards** (#56): drop tables, mob level, and validating `definitionId` against the item catalog.
3. **Level-up grants** (S6): stat/skill points per level. Today `level` only follows EXP.
4. **Materials**: a separate bag (current contract) or inventory stacks?
5. **Quests** model and **exploration** cell ids (both PLACEHOLDER shapes).
6. **HP/MP**: bounds against max HP/MP per level/class and whether they persist across sessions.
7. **Loot stash**: enforce the spec's 8 slots / 24 h expiry (what happens to expired loot?), and the 56 capacity cap. Also the default `warrior` class / `starter_sword` before class choice exists (placeholders).
8. **Stack size limits** per item (table says 50/99). The contract only limits 1–999999.
9. **History retention** for `player_savegame_revisions` / `encounterResults` (200).
10. **HTTPS / auth / hosting**: #39/#56.
11. **JSON Schema library**: review a licence (e.g. JsonSchema.Net) for schema-driven validation, or keep code validation?
12. **CI path filter**: `server-tests.yml` triggers on `server/**` only, so a change to `docs/design/tables/exp_curve.csv`
    (embedded in the server) does not trigger CI. Adding `docs/design/tables/**` and `docs/contracts/**` needs an
    owner-approved `.github` change.

## 12. Verification

Gates run in `server/`:
- `dotnet build -c Release` (warnings as errors);
- `dotnet format --verify-no-changes`;
- `dotnet test` (102 existing + new SaveGame tests);
- `scripts/smoke-local.sh` (real process: savegame, reward, replay, 428/412, restart persistence);
- `dotnet list package --vulnerable --include-transitive`.
