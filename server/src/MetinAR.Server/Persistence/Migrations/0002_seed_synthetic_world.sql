-- METINAR-010: deterministic SYNTHETIC seed data for local development and tests.
-- All coordinates are deliberately placed around "Null Island" (0.0, 0.0) in the Gulf of Guinea,
-- an obviously fake location. No real player, owner or venue location is stored here.
-- Template ids are technical placeholders, not final content names.

INSERT INTO world_entities (id, type, template_id, latitude, longitude, level, aggressive, active, spawned_at, despawn_at) VALUES
    ('seed-mob-0001', 'mob', 'prototype_mob_a', 0.0005,  0.0005, 5, 1, 1, '2026-01-01T00:00:00.000Z', NULL),
    ('seed-mob-0002', 'mob', 'prototype_mob_b', 0.0020, -0.0010, 3, 0, 1, '2026-01-01T00:00:00.000Z', NULL),
    ('seed-npc-0001', 'npc', 'prototype_npc_a', 0.0000,  0.0010, 1, 0, 1, '2026-01-01T00:00:00.000Z', NULL),
    ('seed-mob-0003', 'mob', 'prototype_mob_a', 0.0003,  0.0000, 5, 1, 0, '2026-01-01T00:00:00.000Z', NULL),
    ('seed-mob-0004', 'mob', 'prototype_mob_b', 0.0000, -0.0003, 3, 0, 1, '2026-01-01T00:00:00.000Z', '2026-01-02T00:00:00.000Z'),
    ('seed-mob-0005', 'mob', 'prototype_mob_a', 0.0500,  0.0500, 7, 1, 1, '2026-01-01T00:00:00.000Z', NULL);
