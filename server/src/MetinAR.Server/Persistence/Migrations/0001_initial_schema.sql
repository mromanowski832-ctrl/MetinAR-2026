-- METINAR-010: initial prototype schema.
-- Portable SQL: only types/constructs that also exist in PostgreSQL are used
-- (TEXT, INTEGER, REAL/DOUBLE, CHECK, partial unique index, ON CONFLICT).
-- Timestamps are stored as UTC ISO-8601 text 'yyyy-MM-ddTHH:mm:ss.fffZ' (lexicographically sortable).

CREATE TABLE player_profiles (
    id                       TEXT    NOT NULL PRIMARY KEY,
    display_name             TEXT    NOT NULL,
    level                    INTEGER NOT NULL CHECK (level >= 1),
    current_hp               INTEGER NOT NULL CHECK (current_hp >= 0),
    max_hp                   INTEGER NOT NULL CHECK (max_hp >= 1),
    current_mp               INTEGER NOT NULL CHECK (current_mp >= 0),
    max_mp                   INTEGER NOT NULL CHECK (max_mp >= 0),
    experience               INTEGER NOT NULL CHECK (experience >= 0),
    last_known_latitude      REAL    NULL,
    last_known_longitude     REAL    NULL,
    last_known_position_at   TEXT    NULL,
    created_at               TEXT    NOT NULL,
    updated_at               TEXT    NOT NULL,
    CHECK (current_hp <= max_hp),
    CHECK (current_mp <= max_mp),
    CHECK ((last_known_latitude IS NULL) = (last_known_longitude IS NULL))
);

CREATE TABLE world_entities (
    id           TEXT    NOT NULL PRIMARY KEY,
    type         TEXT    NOT NULL CHECK (type IN ('mob', 'npc')),
    template_id  TEXT    NOT NULL,
    latitude     REAL    NOT NULL CHECK (latitude BETWEEN -90 AND 90),
    longitude    REAL    NOT NULL CHECK (longitude BETWEEN -180 AND 180),
    level        INTEGER NOT NULL CHECK (level >= 1),
    aggressive   INTEGER NOT NULL CHECK (aggressive IN (0, 1)),
    active       INTEGER NOT NULL CHECK (active IN (0, 1)),
    spawned_at   TEXT    NOT NULL,
    despawn_at   TEXT    NULL
);

CREATE INDEX ix_world_entities_lat_lon ON world_entities (latitude, longitude);

CREATE TABLE encounter_sessions (
    id               TEXT NOT NULL PRIMARY KEY,
    player_id        TEXT NOT NULL REFERENCES player_profiles (id),
    world_entity_id  TEXT NOT NULL REFERENCES world_entities (id),
    started_at       TEXT NOT NULL,
    completed_at     TEXT NULL,
    status           TEXT NOT NULL CHECK (status IN ('in_progress', 'completed')),
    CHECK ((status = 'completed') = (completed_at IS NOT NULL))
);

-- At most one open encounter per (player, entity): repeated start calls reuse it.
CREATE UNIQUE INDEX ux_encounter_sessions_open
    ON encounter_sessions (player_id, world_entity_id)
    WHERE status = 'in_progress';

CREATE INDEX ix_encounter_sessions_player ON encounter_sessions (player_id);
