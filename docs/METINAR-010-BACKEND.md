# METINAR-010 — Backend API + persistent player/world-state foundation

Status: prototype, local development only. Issue: #17. Code: [`server/`](../server/README.md).

> **Updated by METINAR-012 (#22):** encounter start now requires a client fix and is checked server-side for proximity;
> completion needs the start nonce and respects a TTL; entities can have deterministic schedules. This document is
> updated in place. The rationale and details are in [METINAR-012-SERVER-AUTHORITY.md](METINAR-012-SERVER-AUTHORITY.md).

> **Updated by METINAR-071 (#71):** additive SaveGame v1 endpoints (`GET`/`PUT /api/v1/player/{playerId}/savegame`
> with revision/ETag concurrency, and `POST /api/v1/encounters/reward`, idempotent by `encounterId`) plus migration `0004`.
> SaveGame v1 has 150 server tests in total. The full contract, local file format and the settled questions (§11) are in [METINAR-071-SAVEGAME-CONTRACT.md](METINAR-071-SAVEGAME-CONTRACT.md).

This is a separate backend workspace. It doesn't touch the Unity client, AR encounter, hand tracking/sword,
GPS provider core, the map provider work or the 3D asset pipeline. The Unity client doesn't call it yet;
this document is the contract a later client integration issue will code against.

## 1. Stack

| Concern | Choice | License |
|---|---|---|
| Runtime | .NET 8 (LTS), ASP.NET Core Minimal API | MIT |
| Storage (dev) | SQLite through `Microsoft.Data.Sqlite` 8.0.11, native bundle `SQLitePCLRaw.bundle_e_sqlite3` 2.1.14 pinned explicitly (the transitive 2.1.6 is flagged by GHSA-2m69-gcr7-jv3q) | MIT / Apache-2.0 |
| OpenAPI | `Swashbuckle.AspNetCore` 6.9.0 (Swagger JSON + UI) | MIT |
| Tests | xUnit 2.9.2, `Microsoft.AspNetCore.Mvc.Testing` 8.0.11 (in-process TestServer) | Apache-2.0 / MIT |

No paid service, cloud account, API key or external network is used at runtime or in tests.
No ORM is used: SQL is plain and portable (see §8), so moving to PostgreSQL only replaces the storage class.

## 2. Architecture

```mermaid
flowchart LR
    subgraph Client["Unity client (future integration, not part of this issue)"]
        GPS["Foreground GPS fix"]
        UI["Map / AR encounter UI"]
    end

    subgraph Server["server/src/MetinAR.Server (localhost only)"]
        direction TB
        EP["Endpoints.cs<br/>/health, /api/v1/*<br/>validation + problem+json"]
        GEO["Geo/GeoDistance<br/>bounding box + haversine<br/>position coarsening"]
        DOM["Domain<br/>PlayerProfile, WorldEntity,<br/>EncounterSession"]
        PORTS["Storage interfaces<br/>IPlayerStore, IWorldStore,<br/>IEncounterStore"]
        SQL["SqliteGameStore"]
        MIG["SchemaMigrator<br/>Persistence/Migrations/*.sql"]
        SW["Swagger / OpenAPI<br/>(Development)"]
    end

    DB[("SQLite file<br/>data/metinar-dev.db")]
    PG[("PostgreSQL<br/>(future, same interfaces)")]

    GPS -- "lat/lon = untrusted input" --> EP
    UI -- "JSON over HTTP /api/v1" --> EP
    EP --> GEO
    EP --> DOM
    EP --> PORTS
    PORTS --> SQL
    SQL --> DB
    MIG -- "startup, once per script" --> DB
    PORTS -. "later swap" .-> PG
    EP --- SW
```

Encounter flow:

```mermaid
sequenceDiagram
    participant C as Client
    participant S as Server
    participant D as SQLite
    C->>S: PUT /api/v1/player/{id} {displayName, lastKnownPosition?}
    S->>D: upsert (position rounded to 3 decimals)
    C->>S: GET /api/v1/world/nearby?lat&lon&radiusMeters
    S->>D: bounding-box query, active + inside spawn window
    S-->>C: entities sorted by distance (stable ids)
    C->>S: POST /api/v1/encounters/start {playerId, worldEntityId}
    S->>D: entity exists? active now? insert or reuse open session
    S-->>C: 201 {encounter, created:true} / 200 {created:false}
    C->>S: POST /api/v1/encounters/complete {encounterId, playerId}
    S->>D: UPDATE ... WHERE status='in_progress' (compare-and-set)
    S-->>C: 200 {encounter, firstCompletion:true}
    C->>S: retry the same complete
    S-->>C: 200 {encounter (unchanged completedAt), firstCompletion:false}
```

## 3. Run locally

See [`server/README.md`](../server/README.md). Short version:

```bash
cd server
dotnet run --project src/MetinAR.Server --launch-profile MetinAR.Server
# http://localhost:5080/health, Swagger UI at http://localhost:5080/swagger
```

- Kestrel listens on `http://localhost:5080` only (`Urls` in `appsettings.json`); `AllowedHosts` is limited to localhost.
- Swagger is on when `ASPNETCORE_ENVIRONMENT=Development` (the launch profile sets it) or `Api__EnableSwagger=true`.
- The SQLite file `server/src/MetinAR.Server/data/metinar-dev.db` is created on first start and kept between restarts.
  It is git-ignored; delete the `data/` folder to reset.

## 4. Data model

All fields are technical and minimal. No classes, currency, skills, loot tables, rewards or monetization are modelled.

### PlayerProfile (`player_profiles`)

| Field | Owner | Notes |
|---|---|---|
| `id` | client chooses (prototype) | `^[A-Za-z0-9_-]{3,64}$`. Becomes an authenticated identity later (§10). |
| `displayName` | client | trimmed, 1–32 printable characters |
| `level`, `hp.current/max`, `mp.current/max`, `experience` | **server** | Set from `PrototypePlayerDefaults` on creation (level 1, HP 100/100, MP 100/100, XP 0). **These are placeholders, not game-design values**; override in config. The API refuses client writes to them. |
| `lastKnownPosition` | client report | **Coarse**: rounded to 3 decimals (~110 m) before storage. Prototype only; not used for any decision. |
| `createdAt`, `updatedAt` | server | UTC |

### WorldEntity (`world_entities`)

| Field | Notes |
|---|---|
| `id` | Stable, server-issued string (seed ids `seed-mob-0001`…). Never reused. |
| `type` | `mob` or `npc` |
| `templateId` | Technical placeholder reference (`prototype_mob_a`, …), not final content |
| `latitude`, `longitude` | WGS84 decimal degrees |
| `level`, `aggressive` | Copied to the client as-is; no balance implied |
| `active` (stored) | Manual on/off switch |
| `spawnedAt`, `despawnAt` | Spawn window `[spawnedAt, despawnAt)`; `despawnAt = null` means no end |

On the wire, `active` is the **effective** state: stored flag on **and** now inside the spawn window.

### EncounterSession (`encounter_sessions`)

| Field | Notes |
|---|---|
| `id` | Server-generated (32 hex chars) |
| `playerId`, `worldEntityId` | FK to the two tables above |
| `startedAt`, `expiresAt` | UTC; `expiresAt = startedAt + EncounterTtlSeconds` (METINAR-012) |
| `completedAt`, `expiredAt` | UTC; at most one of them is ever set, exactly once |
| `status` | `in_progress` → `completed` or `in_progress` → `expired` (one-way) |
| (not on the wire) `nonce_sha256`, `start_distance_meters`, `start_accuracy_meters`, `start_allowed_meters` | replay nonce hash and rounded start evidence. **No client coordinates are stored.** |

A partial unique index allows at most one `in_progress` session per (player, entity). Every status change is also
appended to `encounter_transitions` (from, to, at, reason).

### World entity schedules (`world_entity_schedules`, METINAR-012)

Optional deterministic repeating window per entity: `anchor_at`, `period_seconds`, `active_seconds`. See
[METINAR-012 §4](METINAR-012-SERVER-AUTHORITY.md#4-spawn-scheduling-foundation).

### Seed data

`0002_seed_synthetic_world.sql` adds six entities and `0003_encounter_authority_and_spawn_schedules.sql` two scheduled ones, around **Null Island (0.0, 0.0)** in the Gulf of Guinea, an obviously
synthetic location. No owner, player or real venue location is in the repository.

| id | type | at | purpose |
|---|---|---|---|
| seed-mob-0001 | mob | (0.0005, 0.0005) ~79 m | active, aggressive |
| seed-npc-0001 | npc | (0.0, 0.001) ~111 m | active |
| seed-mob-0002 | mob | (0.002, -0.001) ~249 m | active, passive |
| seed-mob-0003 | mob | (0.0003, 0.0) | stored `active = 0` |
| seed-mob-0004 | mob | (0.0, -0.0003) | despawned 2026-01-02T00:00:00.000Z |
| seed-mob-0005 | mob | (0.05, 0.05) ~7.9 km | outside every allowed radius |
| seed-mob-0006 | mob | (-0.02, 0.0002) ~2.2 km S | `0003`: scheduled, first 5 min of every 10 min |
| seed-npc-0002 | npc | (-0.02, -0.0002) ~2.2 km S | `0003`: scheduled, 06:00–18:00 UTC daily |

## 5. API contract (v1)

Base URL (dev): `http://localhost:5080`. JSON, UTF-8, camelCase. Requests must send `Content-Type: application/json`.
The canonical machine-readable schema is the generated OpenAPI document at `/swagger/v1/swagger.json`.

### 5.1 Versioning

- Game endpoints live under `/api/v1`. `/health` is unversioned.
- Within `v1`, changes are additive only (new optional request fields, new response fields).
  Clients must ignore unknown **response** fields.
- Removing/renaming a field, changing a type or meaning, or tightening validation requires `/api/v2`, served side by side with `v1` during migration.
- **One recorded exception (owner decision D3/Q1, [#33](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316048203)):** METINAR-012 tightened `v1` in place
  (required `clientPosition` with accuracy on start, required `nonce` on complete) because `v1` had no consumers. The
  contract described in this section, as of METINAR-012, is the `v1` baseline; the rules above apply to every later change.
- **Request** bodies are strict: unknown fields are rejected with `400 MALFORMED_REQUEST`. That is how the API refuses
  a client trying to set server-owned fields such as `level`.

### 5.2 Timestamps

Every timestamp is UTC ISO-8601 with millisecond precision and a literal `Z`: `2026-01-01T00:00:00.000Z`.
Clients parse it as UTC and never send local time. Storage uses the same text format.

### 5.3 `GET /health`

`200`:
```json
{ "status": "ok", "serverTime": "2026-10-20T02:36:22.237Z", "apiVersion": "v1", "schemaVersion": "0004_savegame_and_reward_ledger" }
```
`503` with `"status": "degraded"` when the database can't be read.

### 5.4 `PUT /api/v1/player/{playerId}`

Creates the player (`201` + `Location`) or updates the client-editable fields (`200`).

Request:
```json
{ "displayName": "Demo", "lastKnownPosition": { "latitude": 0.0004, "longitude": 0.0004 } }
```
- `displayName` required. `lastKnownPosition` optional; when omitted the previous value is kept.
- Any other field (e.g. `level`, `hp`) → `400 MALFORMED_REQUEST`.

Response (`201`):
```json
{
  "id": "demo-player",
  "displayName": "Demo",
  "level": 1,
  "hp": { "current": 100, "max": 100 },
  "mp": { "current": 100, "max": 100 },
  "experience": 0,
  "lastKnownPosition": { "latitude": 0, "longitude": 0, "reportedAt": "2026-10-20T02:48:04.450Z" },
  "createdAt": "2026-10-20T02:48:04.450Z",
  "updatedAt": "2026-10-20T02:48:04.450Z"
}
```
(The reported 0.0004/0.0004 was coarsened to 0.000/0.000.) `lastKnownPosition` is `null` when never reported.

### 5.5 `GET /api/v1/player/{playerId}`

`200` with the same `PlayerProfile` shape, `404 PLAYER_NOT_FOUND`, or `400 VALIDATION_FAILED` for a malformed id.

### 5.6 `GET /api/v1/world/nearby?lat=&lon=&radiusMeters=&limit=&offset=`

| Param | Required | Range | Default |
|---|---|---|---|
| `lat` | yes | −90…90 | — |
| `lon` | yes | −180…180 | — |
| `radiusMeters` | no | 1…1000 | 250 |
| `limit` | no | 1…100 | 50 |
| `offset` | no | 0…10000 | 0 |

These are technical request limits (config section `ApiLimits`), not gameplay ranges.

Rules:
- Only effectively active entities (§4) are returned.
- Exact great-circle (haversine) distance ≤ `radiusMeters`, after an indexed bounding-box pre-filter.
  Requests near the antimeridian or poles skip the longitude pre-filter, so results stay correct.
- Order: `distanceMeters` ascending, ties broken by `id` (ordinal), so pages are stable for a fixed dataset and time.
- Paging: `totalInRadius` is the full match count; `nextOffset` is the offset of the next page or `null` on the last page.
  Pages are computed per request; entities that spawn or despawn between calls can shift later pages.

Response (`radiusMeters=120&limit=1`):
```json
{
  "center": { "latitude": 0, "longitude": 0 },
  "radiusMeters": 120,
  "serverTime": "2026-10-20T02:48:04.623Z",
  "offset": 0,
  "limit": 1,
  "totalInRadius": 2,
  "nextOffset": 1,
  "entities": [
    {
      "id": "seed-mob-0001", "type": "mob", "templateId": "prototype_mob_a",
      "latitude": 0.0005, "longitude": 0.0005, "level": 5, "aggressive": true, "active": true,
      "spawnedAt": "2026-01-01T00:00:00.000Z", "despawnAt": null, "activeUntil": null, "distanceMeters": 78.6
    }
  ]
}
```
`distanceMeters` is rounded to 0.1 m and is informational. `activeUntil` (METINAR-012) is when the entity stops being
active (end of its scheduled phase or `despawnAt`, whichever is earlier), or `null` for no end.

### 5.7 `POST /api/v1/encounters/start`

Request:
```json
{
  "playerId": "demo-player",
  "worldEntityId": "seed-mob-0001",
  "clientPosition": { "latitude": 0.0005, "longitude": 0.00048, "horizontalAccuracyMeters": 5, "capturedAt": "2026-10-20T03:59:50.900Z" }
}
```
`clientPosition` is **required** (METINAR-012). `latitude`, `longitude` and `horizontalAccuracyMeters` (≥ 0) are required,
and `capturedAt` (UTC) is optional. The fix is evidence only: the server compares it with its own entity position
([METINAR-012 §2](METINAR-012-SERVER-AUTHORITY.md#2-encounter-proximity-validation)). The thresholds come from the
`EncounterRules` configuration and are **prototype values** (D3/Q3), not game balance.

Validation order: body fields (`400`) → player exists (`404 PLAYER_NOT_FOUND`) → entity exists (`404 WORLD_ENTITY_NOT_FOUND`)
→ entity effectively active now, including its schedule (`409 WORLD_ENTITY_INACTIVE`) → fix accuracy
(`422 GPS_ACCURACY_INSUFFICIENT`) → fix age (`422 GPS_FIX_STALE`) → distance (`422 ENCOUNTER_TOO_FAR`).

Responses:
- `201` + `Location` — new session:
  ```json
  {
    "encounter": {
      "id": "d837dca48ac74dfbb0e673f124a7bea0", "playerId": "demo-player", "worldEntityId": "seed-mob-0001",
      "startedAt": "2026-10-20T03:59:51.015Z", "expiresAt": "2026-10-20T04:09:51.015Z",
      "completedAt": null, "expiredAt": null, "status": "in_progress"
    },
    "created": true,
    "nonce": "MO-68u63xGVw9ACE4qNbtWXfCzwD7a19qBcUMz5vrI4"
  }
  ```
- `200` with `"created": false` — an unexpired `in_progress` session for the same (player, entity) already exists and
  is returned with a **new** `nonce` (the previous one stops working). Retrying `start` is therefore safe; always use the
  latest nonce.

### 5.8 `POST /api/v1/encounters/complete`

Request:
```json
{ "encounterId": "d837dca48ac74dfbb0e673f124a7bea0", "playerId": "demo-player", "nonce": "MO-68u63xGVw9ACE4qNbtWXfCzwD7a19qBcUMz5vrI4" }
```
Responses:
- `200` — the stored session plus `firstCompletion`:
  ```json
  {
    "encounter": {
      "id": "d837dca48ac74dfbb0e673f124a7bea0", "playerId": "demo-player", "worldEntityId": "seed-mob-0001",
      "startedAt": "2026-10-20T03:59:51.015Z", "expiresAt": "2026-10-20T04:09:51.015Z",
      "completedAt": "2026-10-20T03:59:51.091Z", "expiredAt": null, "status": "completed"
    },
    "firstCompletion": true
  }
  ```
- `404 ENCOUNTER_NOT_FOUND`; `403 ENCOUNTER_PLAYER_MISMATCH` (another player's session); `403 ENCOUNTER_NONCE_MISMATCH`
  (wrong or superseded nonce); `410 ENCOUNTER_EXPIRED` (reached `expiresAt` before completing; the session becomes `expired`).
  None of these modify a completed session.

No reward, XP, loot or HP change is applied. The endpoint only records completion.

Session lifetime (D3/Q2): an open session ends only by completion or by reaching `expiresAt` (`EncounterTtlSeconds`,
prototype value). If the entity's spawn window or schedule phase ends after the start, the session is **not**
interrupted; completion still works until `expiresAt`. Entity activity is checked only on `start`.

## 6. Idempotency rules

| Operation | Guarantee |
|---|---|
| `PUT /player/{id}` | Same body → same stored state (only `updatedAt` moves). |
| `POST /encounters/start` | At most one open session per (player, entity), enforced by a partial unique index plus `INSERT … ON CONFLICT DO NOTHING`. Retries return the open session with `created:false` and a rotated nonce. After completion or expiry, a new `start` opens a new session. |
| `POST /encounters/complete` | One atomic compare-and-set: `UPDATE … SET status='completed', completed_at=$now WHERE id=$id AND status='in_progress' AND expires_at > $now`. Exactly one call ever gets `firstCompletion: true`. Retries, concurrent calls (tested with 16 in parallel) and calls after a server restart or after the TTL get `firstCompletion: false` and the original `completedAt`. A session that reached its TTL first answers `410` on every call. |

**Rule for future rewards:** grant anything only inside the same transaction that observed the compare-and-set
succeed (`firstCompletion == true`). Never grant on the client's word or on a repeated call. Ideally record the grant in a
table keyed by `encounterId` with a unique constraint, so a second grant can't be written.

## 7. Error format

Every error is `application/problem+json` (RFC 9457) with two extensions: a stable `code` and a `traceId`.
Clients switch on `code` only; `title`/`detail` are human text and may change.

```json
{
  "type": "https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/main/docs/METINAR-010-BACKEND.md#error-codes",
  "title": "One or more request fields are invalid.",
  "status": 400,
  "instance": "/api/v1/world/nearby",
  "code": "VALIDATION_FAILED",
  "traceId": "00-c29da7c11df1cd5f3d3aebb52fcabbac-526f9a0302b24091-00",
  "errors": {
    "radiusMeters": [ "Must be between 1 and 1000." ],
    "limit": [ "Must be between 1 and 100." ]
  }
}
```

### Error codes

| HTTP | `code` | When |
|---|---|---|
| 400 | `VALIDATION_FAILED` | A field is missing or out of range. `errors` maps field → messages. |
| 400 | `MALFORMED_REQUEST` | Unparseable JSON, wrong JSON type, unknown body field, or a non-numeric query value (`lat=abc`). |
| 403 | `ENCOUNTER_PLAYER_MISMATCH` | Completing someone else's encounter. |
| 403 | `ENCOUNTER_NONCE_MISMATCH` | Completion nonce is wrong or was superseded by a later start. |
| 404 | `PLAYER_NOT_FOUND` | Unknown player id. |
| 404 | `WORLD_ENTITY_NOT_FOUND` | Unknown world entity id. |
| 404 | `ENCOUNTER_NOT_FOUND` | Unknown encounter id. |
| 404 | `NOT_FOUND` | Unknown route. |
| 405 | `METHOD_NOT_ALLOWED` | Wrong HTTP method on a known route. |
| 409 | `WORLD_ENTITY_INACTIVE` | Entity exists but is switched off, outside its spawn window or outside its scheduled active phase. |
| 410 | `ENCOUNTER_EXPIRED` | Encounter reached `expiresAt` before completion. |
| 422 | `GPS_ACCURACY_INSUFFICIENT` | Fix accuracy worse than `MaxAcceptedAccuracyMeters`. Extension `maxAccuracyMeters`. |
| 422 | `GPS_FIX_STALE` | `capturedAt` older than `MaxFixAgeSeconds` or too far in the future. Extension `maxFixAgeSeconds`. |
| 422 | `ENCOUNTER_TOO_FAR` | Client fix farther from the entity than allowed. Extensions `distanceMeters`, `allowedMeters`. |
| 400 | `VALIDATION_FAILED` (SaveGame) | Also used for a SaveGame rule violation or a malformed `If-Match`. See [METINAR-071 §5](METINAR-071-SAVEGAME-CONTRACT.md#5-server-api-additive-under-apiv1-d3-rule). |
| 409 | `SAVEGAME_SERVER_OWNED_FIELD_CHANGED` | Sync tried to change server-owned values. Extension `fields`. |
| 409 | `ENCOUNTER_NOT_COMPLETED` | Reward claimed before `POST /encounters/complete`. |
| 409 | `REWARD_NO_SPACE` | Reward doesn't fit in the bag or the loot stash. Nothing recorded. |
| 412 | `SAVEGAME_REVISION_CONFLICT` | `If-Match` is not the current revision. Extension `currentRevision`. |
| 413 | `PAYLOAD_TOO_LARGE` | SaveGame body above `SaveGameLimits:MaxBodyBytes`. |
| 422 | `SAVEGAME_SCHEMA_UNSUPPORTED` | `schemaVersion` not readable. Extensions `receivedSchemaVersion`, `supportedSchemaVersion`. |
| 422 | `SAVEGAME_LOCAL_MODE_NOT_SYNCABLE` | `mode: "local"` sent to sync (local import is #39). |
| 422 | `REWARD_CLAIM_REJECTED` | Claim above the prototype caps (`SaveGameLimits`). Not recorded. |
| 428 | `SAVEGAME_PRECONDITION_REQUIRED` | `PUT savegame` without `If-Match`. |
| 500 | `SAVEGAME_INTEGRITY_FAILED` | Stored save failed checksum/parse/consistency checks. It is left untouched and not overwritten. |
| 500 | `INTERNAL_ERROR` | Unexpected failure. No stack trace or type names are returned. |

## 8. Persistence and migrations

- Schema and seed are versioned SQL files in `server/src/MetinAR.Server/Persistence/Migrations/`, embedded in the
  assembly and applied in filename order on startup by `SchemaMigrator`. Each runs in its own transaction and is
  recorded in `schema_migrations`. Re-running is a no-op.
- New schema changes go in a new numbered file (next: `0005_…sql`; `0004` adds the SaveGame tables and the reward ledger, METINAR-071). Applied files are never edited. `0003` rebuilds
  `encounter_sessions` (SQLite can't alter CHECK constraints) and keeps METINAR-010 rows; legacy open sessions get
  `expires_at = started_at` and no nonce, so they can't be completed.
- SQLite runs in WAL mode with foreign keys on.
- PostgreSQL path: the SQL uses only constructs PostgreSQL also supports (`TEXT`, `INTEGER`, `REAL`, `CHECK`,
  partial unique index, `ON CONFLICT`). The move means a `PostgresGameStore` implementing the same three
  interfaces, `$name` → `@name` parameters, `REAL` → `double precision`, and optionally native `timestamptz` and PostGIS
  for the radius query. The HTTP contract doesn't change.

## 9. Trust boundaries (prototype)

| Input | Trusted? | Today | Future check (not implemented) |
|---|---|---|---|
| `playerId` in path/body | **No** — anyone can claim any id | accepted after format check | derived from an authenticated session/token; never from the body |
| Client GPS (`lat/lon`, `lastKnownPosition`, `clientPosition`) | **No** | range-checked; profile position coarsened; encounter start requires a fix within the allowed distance of the server-owned entity position (capped accuracy tolerance, poor/stale fixes rejected; METINAR-012). Start coordinates are never stored or logged | speed/teleport plausibility between reports, mock-location signals, attestation |
| Encounter outcome | **No** — complete is only "client says it's done" | recorded once via compare-and-set; needs the latest start nonce and must happen before `expiresAt`; nothing granted | server-side combat/reward validation, minimum duration |
| Server-owned stats | n/a | client writes rejected (profile and SaveGame sync, `409`) | — |
| Reward claim (METINAR-071) | **No**, it is a proposal | applied once per `encounterId` only after server-side completion, within prototype caps | server-computed rewards from drop tables (#56) |
| World entities | server-authoritative | seeded via migration; flag + spawn window + deterministic schedule | spawn service with the project brief's safety rules (no roads, railways, water, buildings, private areas): see [METINAR-012 §5](METINAR-012-SERVER-AUTHORITY.md#5-future-integration-points-for-map-safety-data) |

## 10. Security baseline

In this prototype there are, on purpose:
- no authentication, passwords, OAuth, tokens or secrets (nothing to configure, nothing committed);
- no payment data;
- no public deployment: localhost binding, localhost `AllowedHosts`, Swagger only in Development.

**Do not expose this server beyond localhost.** It has no auth and trusts the caller's `playerId`.

Required before any non-local deployment (separate, owner-approved issues):
1. Authenticated player identity (e.g. platform sign-in → server-issued short-lived token); `playerId` comes from the token.
2. TLS, plus restricted CORS/hosts.
3. Rate limiting per identity and per IP (ASP.NET Core `RateLimiter`), with stricter limits on encounter endpoints.
4. Server-authoritative encounter and reward validation (§6 reward rule, §9 future checks).
5. Audit logging of encounter start/complete and any reward grant (who, what, when, from where), with retention rules.
6. Abuse controls: GPS-spoof heuristics, anomaly flags, account suspension tooling.
7. Privacy review of location storage (coarsening level, retention, deletion on request), with no background location.
8. Managed PostgreSQL with backups, secrets in a secret store (never in the repo), dependency vulnerability scanning in CI.

## 11. Verification

Commands (from `server/`) and results at PR time are in the PR description and the issue #17 review comment.
- `dotnet build MetinAR.Server.sln -c Release` — `TreatWarningsAsErrors` + `latest-recommended` analyzers.
- `dotnet format MetinAR.Server.sln --verify-no-changes`.
- `dotnet test MetinAR.Server.sln -c Release` — 102 tests since METINAR-012 (see its §7). The METINAR-010 set: health, OpenAPI document + UI, 404/405 problem+json, nearby query (radius, ordering,
  spawn window, paging, limits), player upsert/validation/coarsening/server-owned fields, encounter start validation
  (unknown / inactive / despawned / unknown player / reuse), idempotent completion (sequential, 16 concurrent, after restart),
  wrong-player completion, migrations once-only, unique open-session constraint, geo math.
  All in-process against temp SQLite files; no network.
- `scripts/smoke-local.sh` — real server process on localhost: health, Swagger, nearby, player, encounter start/complete,
  **process restart**, persistence and idempotency after restart, localhost-only binding.
- `dotnet list MetinAR.Server.sln package --vulnerable --include-transitive` — no known vulnerable packages.

Not part of this issue: Unity compile, Play Mode, Android build and physical-device checks (no client change).
Since METINAR-012 the GitHub Actions workflow `server-tests` builds and runs the server tests on every push/PR that
touches `server/` (see [METINAR-012 §10](METINAR-012-SERVER-AUTHORITY.md#10-ci)).

## 12. Known limitations / out of scope

- No authentication or real identity (by design, §10).
- ~~Client position isn't checked against the entity at encounter start~~ done in METINAR-012 (no speed/teleport plausibility yet).
- ~~No rewards~~ METINAR-071 applies a **client-proposed**, capped reward once per completed encounter. There are no server drop tables, combat or economy yet (#56).
- `offset` paging isn't snapshot-consistent across spawn changes.
- No dynamic spawning; the world is the static synthetic seed (plus deterministic schedules since METINAR-012).
- Unity client integration (HTTP client, DTO mirror) is a separate future issue.
