-- METINAR-012: server-authoritative encounters + deterministic spawn schedules.
-- Portable SQL (also valid PostgreSQL apart from the table-rebuild idiom noted below).

-- 1. Encounter sessions gain TTL, replay nonce (stored only as SHA-256 hash), an 'expired' terminal state
--    and start evidence (distance/accuracy, never client coordinates). SQLite cannot alter CHECK constraints,
--    so the table is rebuilt; on PostgreSQL this would be ALTER TABLE ... ADD COLUMN / DROP CONSTRAINT.
CREATE TABLE encounter_sessions_v2 (
    id                     TEXT NOT NULL PRIMARY KEY,
    player_id              TEXT NOT NULL REFERENCES player_profiles (id),
    world_entity_id        TEXT NOT NULL REFERENCES world_entities (id),
    started_at             TEXT NOT NULL,
    expires_at             TEXT NOT NULL,
    completed_at           TEXT NULL,
    expired_at             TEXT NULL,
    status                 TEXT NOT NULL CHECK (status IN ('in_progress', 'completed', 'expired')),
    nonce_sha256           TEXT NULL,
    start_distance_meters  REAL NULL,
    start_accuracy_meters  REAL NULL,
    start_allowed_meters   REAL NULL,
    CHECK ((status = 'completed') = (completed_at IS NOT NULL)),
    CHECK ((status = 'expired') = (expired_at IS NOT NULL)),
    CHECK (expires_at >= started_at)
);

-- Pre-METINAR-012 rows have no TTL or nonce: they get expires_at = started_at, so an open legacy
-- session can never be completed (conservative).
INSERT INTO encounter_sessions_v2 (id, player_id, world_entity_id, started_at, expires_at, completed_at, expired_at, status)
SELECT id, player_id, world_entity_id, started_at, started_at, completed_at, NULL, status
FROM encounter_sessions;

DROP TABLE encounter_sessions;
ALTER TABLE encounter_sessions_v2 RENAME TO encounter_sessions;

CREATE UNIQUE INDEX ux_encounter_sessions_open
    ON encounter_sessions (player_id, world_entity_id)
    WHERE status = 'in_progress';

CREATE INDEX ix_encounter_sessions_player ON encounter_sessions (player_id);

-- 2. Append-only audit trail of every status transition.
CREATE TABLE encounter_transitions (
    id            TEXT NOT NULL PRIMARY KEY,
    encounter_id  TEXT NOT NULL REFERENCES encounter_sessions (id),
    from_status   TEXT NULL,
    to_status     TEXT NOT NULL CHECK (to_status IN ('in_progress', 'completed', 'expired')),
    at            TEXT NOT NULL,
    reason        TEXT NOT NULL
);

CREATE INDEX ix_encounter_transitions_encounter ON encounter_transitions (encounter_id, at);

-- 3. Deterministic repeating activity windows for world entities (no randomness, no live spawning).
CREATE TABLE world_entity_schedules (
    world_entity_id  TEXT    NOT NULL PRIMARY KEY REFERENCES world_entities (id),
    anchor_at        TEXT    NOT NULL,
    period_seconds   INTEGER NOT NULL CHECK (period_seconds > 0),
    active_seconds   INTEGER NOT NULL CHECK (active_seconds > 0),
    CHECK (active_seconds <= period_seconds)
);

-- 4. SYNTHETIC scheduled entities. Placed ~2.2 km south of Null Island so they never appear in queries
--    centred on (0, 0) with the 1000 m maximum radius used by the METINAR-010 seed checks.
INSERT INTO world_entities (id, type, template_id, latitude, longitude, level, aggressive, active, spawned_at, despawn_at) VALUES
    ('seed-mob-0006', 'mob', 'prototype_mob_b', -0.0200,  0.0002, 3, 1, 1, '2026-01-01T00:00:00.000Z', NULL),
    ('seed-npc-0002', 'npc', 'prototype_npc_a', -0.0200, -0.0002, 1, 0, 1, '2026-01-01T00:00:00.000Z', NULL);

-- seed-mob-0006: active the first 5 minutes of every 10-minute cycle.
-- seed-npc-0002: active 06:00-18:00 UTC every day (anchor 06:00, 12 h of every 24 h).
INSERT INTO world_entity_schedules (world_entity_id, anchor_at, period_seconds, active_seconds) VALUES
    ('seed-mob-0006', '2026-01-01T00:00:00.000Z',   600,   300),
    ('seed-npc-0002', '2026-01-01T06:00:00.000Z', 86400, 43200);
