-- METINAR-035 / #71: persistent SaveGame (contract docs/METINAR-071-SAVEGAME-CONTRACT.md) and the reward ledger.
-- Additive only: no existing table or column changes. Portable SQL (valid PostgreSQL with TEXT -> JSONB if desired).

-- 1. Current SaveGame per player. payload_json is the canonical SaveGame v1 JSON; payload_sha256 (lower-case hex of
--    its UTF-8 bytes) detects corruption on every read. revision is the optimistic-concurrency token (HTTP ETag).
--    A player without a row has a virtual revision-0 save derived from player_profiles.
CREATE TABLE player_savegames (
    player_id       TEXT    NOT NULL PRIMARY KEY REFERENCES player_profiles (id),
    schema_version  INTEGER NOT NULL CHECK (schema_version >= 1),
    revision        INTEGER NOT NULL CHECK (revision >= 1),
    payload_json    TEXT    NOT NULL,
    payload_sha256  TEXT    NOT NULL CHECK (length(payload_sha256) = 64),
    updated_at      TEXT    NOT NULL
);

-- 2. Append-only history of every accepted revision (audit + manual recovery). Never updated or deleted by the server.
CREATE TABLE player_savegame_revisions (
    player_id       TEXT    NOT NULL REFERENCES player_profiles (id),
    revision        INTEGER NOT NULL CHECK (revision >= 1),
    schema_version  INTEGER NOT NULL,
    payload_json    TEXT    NOT NULL,
    payload_sha256  TEXT    NOT NULL,
    reason          TEXT    NOT NULL CHECK (reason IN ('client_sync', 'encounter_reward')),
    encounter_id    TEXT    NULL REFERENCES encounter_sessions (id),
    created_at      TEXT    NOT NULL,
    PRIMARY KEY (player_id, revision)
);

-- 3. Reward ledger: at most one reward per encounter session, ever (the PRIMARY KEY is the idempotency guarantee).
CREATE TABLE encounter_rewards (
    encounter_id      TEXT    NOT NULL PRIMARY KEY REFERENCES encounter_sessions (id),
    player_id         TEXT    NOT NULL REFERENCES player_profiles (id),
    claim_json        TEXT    NOT NULL,
    granted_json      TEXT    NOT NULL,
    applied_revision  INTEGER NOT NULL,
    applied_at        TEXT    NOT NULL
);

CREATE INDEX ix_encounter_rewards_player ON encounter_rewards (player_id, applied_at);
