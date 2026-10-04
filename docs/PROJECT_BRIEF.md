# MetinAR project brief

MetinAR is a real-world GPS + augmented reality RPG.

The real world is the game map.

## Core example

- Player walks in the real world.
- A notification may say: "Venomous Spider — 50 m away."
- Monsters exist at geographic locations.
- Player sees them on a GPS map.
- Player physically approaches the monster.
- When close enough, AR combat can be started.
- The phone camera shows the real environment.
- The monster appears on the real-world ground.
- The phone tracks the player's real hand.
- A virtual sword is attached to the tracked hand.
- Physical hand movement is used to attack the monster.

## World rules

- NPCs can have permanent real-world GPS locations.
- Monsters can spawn dynamically.
- Dynamic spawning must avoid unsafe or inaccessible locations.
- No spawns on vehicle roads.
- No spawns on railway tracks.
- No spawns in water.
- No spawns inside buildings.
- No spawns on inaccessible or private areas where possible.
- Safety buffers around dangerous areas are required.
- GPS is used for world navigation.
- Local AR tracking is used for combat positioning.

## Prototype 001 goal

GPS, then a real-world map, then a test monster, then distance to the monster, then the player approaches the monster, then an AR encounter becomes available, then ground detection, then the monster is placed on the ground, then hand tracking, then a virtual sword is attached to the hand, then sword collision or attack, then the monster loses HP, then the monster dies.

## Development rules

- Build from scratch.
- Do not invent gameplay systems unless requested.
- No Metin2 code or copyrighted assets.
- No paid dependencies without approval.
- No production deployment without approval.
- No major architectural decisions without approval.
- Report errors immediately.
- Never claim something was tested if it was not tested.
- Keep systems modular.
