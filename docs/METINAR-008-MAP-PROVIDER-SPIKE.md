# METINAR-008: Real-world map provider spike (research and decision)

Issue: #9. Branch: `docs/008-map-provider-spike`. Base: `main` @ `8557a3e`.
Type: **documentation and research only**. No accounts, API keys, billing, SDK or package installs, tile copying,
Unity scene changes, or changes to AR encounter / hand tracking / sword / combat / GPS provider core.

All sources were accessed on **2026-10-20**. The full list is in [Sources](#sources).
Anything not confirmed from a primary source is marked **UNVERIFIED**. Nothing here was compiled or run in Unity.
Unity `6000.6.4f1` compatibility statements come from vendor documentation only, and no candidate
certifies Unity 6.6 by name.

---

## 0. TL;DR

| | Choice |
|---|---|
| **Recommended first choice** | **OSM-derived vector tiles (OpenMapTiles schema), rendered by a thin MetinAR-owned Unity C# mesh builder behind a provider interface.** Prototype against the OpenFreeMap public instance (free, no key, commercial use allowed). Production on self-hosted PMTiles built with Planetiler, so we control cost, privacy and caching. |
| **Recommended fallback** | **Mapbox Unity SDK v3.1** (`com.mapbox.sdk`). It is maintained, URP, iOS/Android, and has a location-based-game sample and AR parent-transform support. Downsides: account and token required, Unity pricing is "contact sales", Unity 6 is "best-effort", and the license is tied to the Mapbox TOS. |
| **Not recommended for the GPS map** | Google Maps Platform (no Unity SDK since 2022; Photorealistic 3D Tiles return **403 for EEA-billed projects**; no caching/offline; no "machine interpretation", so it can't feed spawn filtering). Cesium for Unity (globe/photoreal oriented; ion free tier is non-commercial; heavy native package). ArcGIS Maps SDK for Unity (Unity 6.6 not on the certified list; account required). Niantic Maps SDK (**sunset, deactivated Oct 2025**). |
| **Spawn safety** | Keep it **independent of the render provider**: an OSM-derived "spawn safety" dataset (roads, rail, water, buildings, access=private/no, sensitive land use), queried through `ISpawnSafetyService`, default-deny when data is unknown. |

---

## 1. Requirements

From issue #9 (owner-confirmed) and the existing code:

| # | Requirement | Source |
|---|---|---|
| R1 | The real physical world is the game map. | Issue #9 |
| R2 | Angled, rotating GPS map with a full 3D player avatar. | Issue #9 |
| R3 | Mobs and NPCs are 3D world entities at geographic coordinates. | Issue #9 |
| R4 | Custom MMORPG-style presentation, not a navigation-app look. Full control of styling/materials. | Issue #9 |
| R5 | Android **and** iOS. | Issue #9 |
| R6 | Future core requirement: filter unsafe/private spawns. | Issue #9 |
| R7 | Unity `6000.6.4f1`, URP 17.6.0, AR Foundation/ARCore/ARKit 6.6.2, IL2CPP ARM64, Android minSdk 31. | `client/Packages/manifest.json`, `docs/METINAR-004.md` |
| R8 | Must not break the current location contract: `GeoCoordinate`/`GeoMath` unchanged; `WorldMapOrigin` maps geo ↔ local meters (+X east, +Z north, 1 unit = 1 m), captured once from the first fix. | `Scripts/WorldMap/WorldMapOrigin.cs`, `docs/METINAR-004.md` |
| R9 | Foreground location only; no `ACCESS_BACKGROUND_LOCATION`. | `docs/AUTONOMOUS_WORKFLOW.md`, METINAR-004B |
| R10 | Map code separate from AR combat. Today the only bridge is `ArEncounterHandoff.Submit(ArEncounterRequest)`. | `Scripts/ArEncounter/ArEncounterHandoff.cs`, `WorldMapPrototypeController.cs` |
| R11 | No silent commitment to a paid provider; cost/licensing needs owner approval. | Issue #9, workflow approval boundaries |
| R12 | Owner and likely first market are in Poland (**EEA**). This matters for Google's EEA terms. | Owner context; Google EEA notice |

Derived technical requirements:

- Gameplay scale around the player: roughly 150–500 m radius at street level (web-mercator zoom ~15–17). Prototype values are 12 m aggro range and 13 m mob offset.
- Tile data must be usable as **game geometry** (meshes and colliders in Unity world space), not only as a 2D image.
- Attribution must be shown in-game in a way that fits the HUD.
- Mobile budget: the current dev APK is 51.1 MB (METINAR-004B). Map runtime additions should be measured and kept small.

---

## 2. Candidates and findings

### 2.1 OSM-derived vector tiles + MetinAR-owned Unity renderer (custom pipeline)

**Data/tiles options (all OSM-derived, ODbL):**

| Source | What it is | Cost / limits | Offline / cache | Attribution |
|---|---|---|---|---|
| **OpenFreeMap public instance** | Planet vector tiles, OpenMapTiles schema, weekly builds; TileJSON `https://tiles.openfreemap.org/planet/latest` | Free. "No limits on the number of map views or requests", no registration, no API keys, no cookies; **commercial use: "Yes."** No SLA. Funded by donations; "may discontinue it at any time without notice" (TOS, updated 2026-10-14). | TOS allows app offline downloads of **≤3,000 tiles or 50 MB per download, ≤4 parallel requests, unique User-Agent**. Uses Cloudflare CDN. | "OpenFreeMap © OpenMapTiles Data from OpenStreetMap" (OpenFreeMap part optional). |
| **Self-hosted PMTiles** (Protomaps basemap or own Planetiler build) | Single-file tile archive served from static storage via HTTP range requests | Planet ≈120 GB (z0–15) per Protomaps docs; regional extracts via `pmtiles extract` (each extra zoom ≈ doubles size). Hosting/egress cost depends on host: **UNVERIFIED**, needs a quote. | Under our control; can ship regional packs. | ODbL Produced Work → OpenStreetMap attribution required. |
| **Own Planetiler build from Geofabrik extract** | `poland-latest.osm.pbf` (2.0 GB, data to 2026-10-18) → OpenMapTiles-schema MBTiles/PMTiles | Compute + storage only. Planetiler is Apache-2.0. | Under our control. | OSM + (if OpenMapTiles schema) "© OpenMapTiles" (schema design CC-BY 4.0). |
| OSMF `tile.openstreetmap.org` / `vector.openstreetmap.org` | Community servers | Free but **best effort**; may be blocked without notice; commercial services warned that access "may be withdrawn at any point". | **Bulk download and offline prohibited.** Must cache ≥7 days / honour headers; unique User-Agent. | Required. |

→ OSMF servers are **not suitable** for a game backend. They're fine for occasional manual checks only.

**Unity rendering options:**

- **Own thin renderer (recommended):** decode Mapbox Vector Tile (MVT/PBF) in C#, triangulate polygons, build line ribbons for roads, extrude buildings, and emit Unity meshes under a `MapRoot` in `WorldMapOrigin` local meters. Candidate libraries (license verified on GitHub): `mapbox/vector-tile-cs` (BSD-3-Clause, last push 2026-06-29), `mapbox/earcut` port or similar for triangulation (earcut is ISC). Pure managed code with no native plugins, so IL2CPP ARM64 / iOS issues are limited to normal C# stripping. Compatibility with 6000.6.4f1 is **expected but UNVERIFIED** until compiled.
- **`KazukiKuriyama/maplibre-unity` (community port, not a MapLibre-org project):** pure C# port of MapLibre GL JS for Unity. Style spec v8, vector/PBF, PMTiles/MBTiles, fill-extrusion, DEM terrain, pan/zoom/rotate/pitch, and `PrefabSource` anchoring GameObjects to a `LngLat`. MIT, v1.0.0 (2026-05-09), last push 2026-10-12, **8 stars**, single maintainer. Requires Unity 6 (6000.3.10f1+) and URP 17.x. Its own docs say **mobile is untested**. Worth a time-boxed evaluation in the POC, but too young to be a production dependency without our own fork.
- **MapLibre Native** (official, BSD-2, very active) has Android/iOS SDKs but **no Unity binding**. Embedding a native map view under Unity would block a free 3D game camera and custom entities. Not practical for R2–R4.

| Criterion | Finding |
|---|---|
| Unity 6000.6.4f1 | Own renderer: no native code, so low risk (UNVERIFIED until compiled). maplibre-unity: requires 6000.3.10f1+, 6.6 UNVERIFIED. |
| Maintenance | Own code: maintained by us. Data toolchain (Planetiler, PMTiles, OpenFreeMap) all active in Sep–Oct 2026. |
| Android / iOS | Pure C#, so both are expected to work (UNVERIFIED on device). |
| 3D terrain / buildings / styling | Buildings: OpenMapTiles `building` layer with heights where tagged. Styling: total control (our own URP materials). That is the strongest fit for R4. Terrain: optional later via open terrain-RGB/Terrarium tiles (e.g. Mapterhorn, referenced by Protomaps docs; licensing **UNVERIFIED**). Flat ground is fine for a first city map. |
| Rotate / tilt / custom 3D entities | Native. Map meshes and game entities live in the same Unity world space, and the existing `MapCameraFollow` keeps working. |
| Offline / cache | Self-hosted: unrestricted (our own data). OpenFreeMap: limited offline downloads per TOS (above). |
| Attribution | "© OpenStreetMap contributors" linked to openstreetmap.org/copyright (+ "© OpenMapTiles" if that schema is used). The OSMF guideline allows games to attribute in game view, splash screen or credits, as long as the details stay findable. |
| Licensing | OSM data **ODbL**. Rendered map = Produced Work (attribution). A **derivative database** used publicly (e.g. our own safety polygons derived from OSM) must be offered under ODbL. See §6. |
| Pricing | Prototype: €0 with OpenFreeMap. Production: hosting/egress only (UNVERIFIED amount). No per-user fee. |
| Commercial use | Allowed (ODbL; OpenFreeMap TOS "commercial usage allowed: Yes"). |
| Performance / APK size | Small code size (managed C# only; delta UNVERIFIED). Runtime cost is mesh building on tile load, done off the main thread where possible. Needs device measurement. |
| Privacy | Tile requests reveal approximate area (z/x/y) + IP to the tile host. Self-hosting keeps this first-party. OpenFreeMap goes through Cloudflare. |
| Safe-spawn suitability | **Best.** Same OSM tags (highway, railway, water, building, landuse, access) are available raw, and we're allowed to analyse them (ODbL). |
| Lock-in | Lowest. Open formats (MVT, PMTiles, OpenMapTiles schema). Host can be swapped by changing a URL. |
| Main risk | We own the renderer: engineering effort for labels, road ribbons, LOD, tile seams. OpenFreeMap has no SLA. |

### 2.2 Mapbox Unity SDK v3.x

| Criterion | Finding |
|---|---|
| Status | `mapbox/mapbox-unity-sdk`, package `com.mapbox.sdk`, **v3.1.0 tagged 2026-05-18**, repo pushed 2026-10-19. Maintainer (Mar 2026): v3 is "officially on general availability stage but it's still under development". |
| Unity 6000.6.4f1 | README: 2022.3 LTS = primary target; **6000.x (Unity 6) = "Best-effort"**. 6.6 specifically UNVERIFIED. Past issues include "Not compatible with Unity 6" (closed 2025-01) and an open "New Input system issue with v3.1.0" (2026-06). v3.1 adds `com.unity.burst@1.8.12` and soft-depends on the Input System (MetinAR has inputsystem 1.20.0). |
| Android / iOS | Only iOS and Android are supported build targets (+ Editor play mode). Android needs project-side `Plugins/` copy from `Runtime/AndroidBuildSettings/6000/`. IL2CPP stripping: Low tested, Medium "seems to work", High untested. |
| 3D / styling | Terrain-RGB elevation meshes, vector layers → GameObjects/meshes, imagery styles, URP required. Custom materials are possible via the vector layer module. |
| Rotate / tilt / entities | v3.1 camera system with touch pan/pinch/tilt; **"AR/MR parent-transform support: MapRoot can be translated and rotated under an external rig"**; "LocationBasedGame" sample referenced in migration guide. |
| Offline / cache | SQLite tile cache (IL2CPP fix in v3.1). Offline/caching **terms for Unity: UNVERIFIED** (governed by Mapbox TOS / product terms). |
| Attribution | Required by Mapbox TOS. Exact Unity requirement **UNVERIFIED** (typically Mapbox wordmark + "© Mapbox © OpenStreetMap"). |
| Licensing | SDK source is **not open source**: "licensed under the Mapbox TOS for use only with the relevant Mapbox product(s)… terminates automatically if a developer no longer has a Mapbox account in good standing". Modifying billing/telemetry code is not allowed. |
| Pricing | Unity pricing page: **"For pricing on the Maps SDK for Unity, contact Mapbox Sales."** Docs: mobile Unity apps are metered by **MAU**. For reference only, native Maps SDKs for Mobile list free up to 25,000 MAU, then $4.00 / 1,000 MAU (25k–125k), but **applicability to Unity is UNVERIFIED**. Without a card, the trial is limited to 100 mobile MAU / 20,000 vector tile requests per month. |
| Commercial | Allowed under paid/free-tier account per TOS. Requires an account and token (**owner approval needed**). |
| Performance / APK | Native SQLite libs + Burst. APK delta **UNVERIFIED**. |
| Privacy | License: "the SDK sends limited de-identified location and usage data". Mapbox privacy FAQ: processing in AWS-US; **cannot limit processing to the EU**; GDPR via DPA/SCCs. Must be covered in our privacy policy. |
| Safe-spawn | Possible in-app via vector features, but whether Mapbox data may be used for non-display analysis is **UNVERIFIED**. Recommendation: do safety from OSM regardless (§7). |
| Lock-in | High: proprietary SDK license + token + Mapbox tilesets/styles. Migration means rewriting the map layer (mitigated by §6 interfaces). |

### 2.3 Google Maps Platform

| Criterion | Finding |
|---|---|
| Unity SDK | **Maps SDK for Unity / gaming services decommissioned 2022-12-31** ("due to limited adoption"). There is no current Google Unity map SDK. |
| Current route | Map Tiles API: 2D tiles (raster) or **Photorealistic 3D Tiles** (OGC 3D Tiles/glTF) through a third-party renderer, e.g. Cesium for Unity. ARCore Geospatial Creator uses the same 3D tiles. |
| **EEA (Poland)** | For projects created after 2025-07-08 linked to an **EEA billing account**: "**Photorealistic 3D tiles are not available. The Map Tiles API will throw a 403**". The same applies to satellite 2D tiles. **That rules out the main reason to use Google here.** |
| Styling | Photorealistic 3D tiles are photoreal meshes that can't be restyled. That conflicts with R4. 2D raster tiles are images only. |
| Custom entities | Allowed over 3D tiles if the objects aren't "extracted, traced, or otherwise derived" from them. |
| Offline / cache | "You must not pre-fetch, index, store, or cache any Content" except per terms; respect Cache-Control. **Offline use prohibited.** |
| Non-visual use | Map Tiles API may **not** be used for "machine interpretation", "object detection", "geodata extraction" etc. **So it can't feed spawn-safety filtering.** |
| Pricing (global list) | 2D Map Tiles: 100,000 free/month, then $0.60/1,000. Photorealistic 3D Tiles: 1,000 root requests free/month, then $6.00/1,000 (one root request = up to 3 h session). Billing must be enabled. Quotas: 15,000 2D tiles/day/project (default), 10,000 3D root requests/day. |
| Attribution | Google Maps logo + per-tile data attributions, aggregated on screen. |
| Lock-in | High, plus EEA terms risk. |

### 2.4 Cesium for Unity (+ Cesium ion)

| Criterion | Finding |
|---|---|
| Status | `CesiumGS/cesium-unity` (Apache-2.0), **v1.26.0 released 2026-09-29/10-01**, monthly releases. Requires Unity 2022.3+ / Unity 6. **Unity 6.5 support added in v1.25.0 (2026-08-03)**. 6.6 not mentioned: **UNVERIFIED**. |
| Android / iOS | Android ARM64/x86-64 (IL2CPP, no ARMv7), iOS supported. Android native lib built with 16 KB page alignment since v1.23.3. |
| 3D / styling | Full-scale WGS84 globe, 3D Tiles streaming (Google photoreal, Cesium OSM Buildings, own tilesets), GeoJSON raster overlays and vector styling (v1.23+). Built for geospatial/digital-twin realism rather than stylized game maps. |
| Entities | `CesiumGeoreference` and globe anchors place GameObjects at lat/lon/height. |
| Licensing / pricing | Plugin is free (Apache-2.0). **Cesium ion Community (free) is for "personal and non-commercial use"** or exploratory commercial evaluation. Paid account required if revenue/funding > $50K or usage exceeds limits. Commercial plan $149/month individual. "Integration pricing" for solutions used outside the organization: contact sales. You can avoid ion by pointing at your own 3D Tiles. |
| Google tiles via Cesium | Same **EEA 403** issue as §2.3. |
| APK / performance | Release tarball `com.cesium.unity-1.25.0.tgz` is **566 MB** (all platforms). Per-APK delta **UNVERIFIED**, likely significant native code. Globe streaming has a higher GPU/bandwidth cost than flat local tiles (**UNVERIFIED** on our devices). |
| Safe-spawn | Not a data source by itself. |
| Verdict | Good for photoreal AR/digital twin. Poor fit for a stylized, flat-ish GPS game map in the EEA. |

### 2.5 ArcGIS Maps SDK for Unity (Esri) (extra candidate, suggested by Niantic's sunset notice)

| Criterion | Finding |
|---|---|
| Status | 2.4.0 released 2026-08 (search result: 2026-08-05). Certified for **Unity 6.5, 6.3 LTS, 6.0 LTS**; "other versions may work but are not tested or certified". **6.6 not certified.** |
| Android / iOS | Android 9–17 (API 27–37; Android 9 deprecated), iOS/iPadOS 18 and 26. |
| Pricing / account | SDK is a free Asset Store download (search result). Requires ArcGIS location services/API key. **Pricing and terms for game use: UNVERIFIED.** |
| Verdict | Enterprise GIS oriented, account-bound, not certified for our Unity version. Not shortlisted. |

### 2.6 Niantic Maps SDK / Lightship / 8th Wall maps

- **Niantic Maps SDK for Unity: sunset.** No updates; support ended July 2025; docs and GitHub removed Aug 2025; **SDK deactivated October 2025** (endpoints shut down). Niantic itself points to Mapbox or Esri.
- Niantic Spatial NSDK 4.0 (VPS/AR localization) is still alive but is not a map renderer. Lightship.dev was decommissioned (2026-02-27 per NSDK 3.17 page; migration guide says self-serve until 2027-02-20). Not relevant to the GPS map screen.
- 8th Wall: not evaluated further (web AR, not a Unity map SDK). **UNVERIFIED / out of scope.**

### 2.7 Comparison matrix

Legend: ✅ good, ⚠️ caveat, ❌ blocker/poor, ❓ UNVERIFIED.

| Criterion | OSM tiles + own renderer | maplibre-unity (community) | Mapbox Unity v3.1 | Google Map Tiles (+Cesium) | Cesium for Unity (ion) | ArcGIS Unity 2.4 | Niantic Maps |
|---|---|---|---|---|---|---|---|
| Unity 6000.6.4f1 | ✅❓ pure C# (compile unverified) | ⚠️ 6000.3.10f1+, 6.6 ❓ | ⚠️ Unity 6 "best-effort" | n/a (renderer-dependent) | ⚠️ 6.5 added, 6.6 ❓ | ❌ 6.6 not certified | ❌ dead |
| Maintenance | ✅ ours + active OSS toolchain | ⚠️ 1 maintainer, 8★ | ✅ active, GA "still under development" | ❌ no Unity SDK since 2022 | ✅ monthly releases | ✅ | ❌ deactivated 2025-10 |
| Android / iOS | ✅❓ | ⚠️ mobile untested | ✅ (Android setup steps) | via Cesium ✅ | ✅ (no ARMv7) | ✅ | ❌ |
| Stylized MMORPG look (R4) | ✅ full control | ✅ style.json | ✅/⚠️ via modules | ❌ photoreal/raster | ⚠️ realism-oriented | ⚠️ | ❌ |
| Buildings / terrain | ✅ buildings, terrain later | ✅ extrusion + DEM | ✅ terrain-RGB + vectors | ✅ photoreal 3D (❌ in EEA) | ✅ | ✅ | ❌ |
| Rotate / tilt / 3D entities | ✅ | ✅ PrefabSource | ✅ camera + AR parent transform | ✅ | ✅ | ✅ | ❌ |
| Offline / cache | ✅ self-host; ⚠️ OpenFreeMap ≤3,000 tiles/50 MB | ✅ PMTiles/MBTiles | ⚠️ SQLite cache; terms ❓ | ❌ prohibited | ⚠️ depends on source | ❓ | ❌ |
| Attribution | OSM (+OpenMapTiles) | OSM (+source) | Mapbox + OSM ❓ | Google logo + data credits | per source | Esri ❓ | n/a |
| Data license | ✅ ODbL (share-alike for derived DBs) | ✅ ODbL | ⚠️ Mapbox TOS | ❌ restrictive | per source | ❓ | n/a |
| Price / free tier | ✅ €0 prototype; hosting only | ✅ same as data | ⚠️ "contact sales"; trial 100 MAU | ⚠️ pay-per-use; billing required | ⚠️ ion free = non-commercial | ❓ | n/a |
| Commercial use | ✅ | ✅ (MIT) | ✅ with account | ✅ with billing (EEA limits) | ⚠️ paid plan | ❓ | n/a |
| APK / perf | ✅ small code; perf ❓ | ✅❓ | ⚠️ natives + Burst ❓ | ⚠️ Cesium natives | ⚠️ heavy ❓ | ❓ | n/a |
| Privacy | ✅ first-party when self-hosted | ✅ same | ⚠️ telemetry, US processing | ⚠️ Google | ⚠️ ion | ❓ | n/a |
| Safe-spawn data | ✅ OSM raw tags | ✅ | ⚠️ analysis rights ❓ | ❌ forbidden | ❌ not a data source | ❓ | n/a |
| Lock-in | ✅ lowest | ✅ low (MIT, forkable) | ❌ high | ❌ high | ⚠️ medium | ❌ high | n/a |
| Needs account/key now | ❌ no | ❌ no | ✅ yes | ✅ yes + billing | ✅ (unless own tiles) | ✅ | n/a |

---

## 3. Prototype and production cost risks

All amounts in USD as published on 2026-10-20. Not quotes. Only the first column needs no owner money decision.

| Option | Prototype (owner phone + testers) | Production risk drivers | Worst-case surprise |
|---|---|---|---|
| OSM + OpenFreeMap (prototype) | **$0**, no account | No SLA. Service may stop without notice. Must respect offline limits and a unique User-Agent. | Map goes blank if OpenFreeMap blocks or ends. Mitigated by a configurable tile URL and a self-host fallback. |
| OSM + self-hosted PMTiles | Storage/egress for a Poland extract (**UNVERIFIED**, needs host quote). Build compute is ours. | Egress grows with players × tiles × cache misses. Planet-scale ≈120 GB archive. Ops effort for weekly data refresh. | Bandwidth bill growth. Mitigated by aggressive client cache, CDN, and regional packs. |
| Mapbox Unity | Trial without card: 100 MAU / 20k vector tiles per month. Adding a card unlocks "free tiers" (Unity-specific numbers **UNVERIFIED**, "contact sales"). | Per-MAU billing at scale. Native-mobile list price (reference only): $4.00/1,000 MAU after 25k. | Viral growth = large monthly bill. Token leaks get billed to the owner. Pricing can change. |
| Google Map Tiles | Billing account required even for free caps. | EEA: 3D tiles unavailable. 2D tiles $0.60/1,000 after 100k/month. 15k/day default quota. | Feature unavailable in EEA. Usage-based bill. |
| Cesium ion | Free Community tier only for non-commercial/evaluation | $149–$874+/month for commercial. "Integration pricing" for external apps via sales. | License tier mismatch for a commercial game. |

Engineering cost risk (the trade-off of the first choice): building and maintaining our own tile → mesh renderer (roads, polygons, buildings, labels, tile seams, LOD, cache). Rough guess **2–4 agent issues** to reach prototype parity with the current flat ground, sized by the POC (§8). This is an **estimate, UNVERIFIED**.

---

## 4. Recommended first choice

**OSM-derived vector tiles (OpenMapTiles schema) + MetinAR-owned Unity mesh renderer behind provider-independent interfaces.**

- **Prototype data:** OpenFreeMap public instance. Free, no key, commercial use allowed. Unique User-Agent `MetinAR/<version> (+contact)`, attribution shown, tile URL configurable (not hard-coded).
- **Production data:** self-hosted PMTiles (Planetiler build from Geofabrik extracts, starting with Poland), on static storage + CDN chosen by the owner.
- **Why:** it is the only option that meets all of R4 (full stylization), R6 (we're allowed to analyse the data), R11 (no paid commitment now), R12 (no EEA feature restrictions), and has the lowest lock-in. It also works without any account or key, so the POC can run under the current approval boundaries.
- **Optional shortcut evaluated in the POC:** `maplibre-unity` (MIT). If it runs well on the owner's Android phone it could replace parts of our renderer. Otherwise we keep our thin renderer. Either way it sits behind the same interfaces.

## 5. Recommended fallback

**Mapbox Unity SDK v3.1**, used only if the POC shows the custom renderer is too costly, **and** after the owner approves creating a Mapbox account/token and gets Unity pricing from Mapbox Sales. It is the most complete maintained Unity-native GPS-game map SDK found (touch camera, terrain, vector layers, location-based-game sample, AR parent transform). Known risks: "best-effort" Unity 6 support, proprietary license tied to an account in good standing, MAU billing, US data processing. Because of §6 interfaces, switching to it later affects only the map adapter layer, not gameplay.

---

## 6. Provider-independent architecture

### 6.1 Layers and ownership

```
MetinAR.Location            (existing, unchanged)  GPS → LocationFix / GeoCoordinate
        │
        ▼
MetinAR.WorldMap            (existing)  WorldMapOrigin (geo ↔ local m), WorldMapEntity,
        │                                PlayerMapAvatar, MapCameraFollow, PrototypeWorldPlacement
        │  uses only interfaces ▼
MetinAR.Map.Abstractions    (new, pure C#, no UnityEngine where possible)
        │   IMapTileSource      : Task<byte[]> GetTileAsync(TileId z/x/y, CancellationToken)
        │   IMapFeatureDecoder  : bytes → MapFeature { Layer, Class, Geometry (lon/lat), Props }
        │   IMapRenderer        : builds/destroys tile GameObjects under MapRoot (local meters)
        │   IMapAttribution     : string[] currently required credits + "more info" URL
        │   IGroundHeightProvider : float HeightAt(GeoCoordinate)   (flat 0 m first)
        │   ISpawnSafetyService : SafetyResult Evaluate(GeoCoordinate, radiusMeters)
        │   TileMath (web mercator z/x/y ↔ lon/lat) + WorldMapOrigin projection (pure, tested)
        ▼
MetinAR.Map.Osm             (new adapter)  HTTP/PMTiles tile source, MVT decoder, URP mesh builder,
                                           MapStyleProfile (ScriptableObject: game colours/materials)
MetinAR.Map.Mapbox          (fallback adapter, only if approved)
```

Rules:

1. **Gameplay never sees provider types.** Mobs, NPCs, quests, spawns and aggro use `GeoCoordinate` (and later server IDs) only. `WorldMapEntity` converts to Unity space through `WorldMapOrigin`, exactly as today.
2. **One coordinate frame.** The map renderer emits meshes in the same ENU local-meter frame as `WorldMapOrigin` (+X east, +Z north, 1 unit = 1 m). Map tiles are projected through `WorldMapOrigin.WorldToLocal`, so players, mobs, NPCs and map geometry line up by construction. This also keeps provider-specific projections (Mapbox/Cesium `MapRoot`, georeference) inside the adapter.
3. **Camera stays game-owned.** Angle, rotation and follow are `MapCameraFollow`'s job (R2). The provider never owns input or camera.
4. **Large movement:** `WorldMapOrigin` is capture-once today. Float precision is fine for a few km. A future issue should add an origin **rebase** event (shift `MapRoot` + entities) when the player moves > ~5–10 km. Not needed for the POC.
5. **Attribution is a HUD element** fed by `IMapAttribution` (corner text + "(i)" → licences panel), so it follows the OSMF games guideline and any provider's rules.
6. **Tile caching** lives in the tile source adapter and follows each provider's rules (HTTP cache headers; OpenFreeMap offline limits; none for Google).

### 6.2 Boundary with AR combat (R10)

- Map code (`MetinAR.WorldMap`, `MetinAR.Map.*`) **must not reference** `MetinAR.ArEncounter`, hand tracking or sword/combat types. The **only** bridge stays the existing one: the world-map controller builds an `ArEncounterRequest` and calls `ArEncounterHandoff.Submit(...)` when an encounter starts. The AR side consumes it with `TryConsume`.
- AR code must not reference map/provider types. If AR ever needs location context, pass plain data (e.g. `GeoCoordinate`, mob ID) inside the request, never map objects.
- Suggested enforcement (future issue, not done here): asmdefs `MetinAR.Map.Abstractions` (no references), `MetinAR.Map.Osm` → Abstractions, `MetinAR.WorldMap` → Location + Map.Abstractions, `MetinAR.ArEncounter` → no Map references. The project currently has **no asmdefs** (METINAR-004 note), so this is a separate decision.

### 6.3 How player, mobs and NPCs overlay the map

1. First valid fix → `WorldMapOrigin.CaptureOrigin` (unchanged).
2. Map system requests tiles covering radius *R* around the player at zoom *z* (POC: z15–16, 3×3 tiles), projects them to local meters, and builds ground/road/water/building meshes at Y≈0 (or `IGroundHeightProvider` later).
3. `PlayerMapAvatar` follows fixes; mobs/NPCs are `WorldMapEntity` at their `GeoCoordinate`. Y comes from `IGroundHeightProvider` (0 for flat map).
4. Buildings render with a game material (optionally semi-transparent / height-clamped near the player) so 3D entities aren't hidden. Mob/NPC colliders and click targets (`MobClickTarget`) stay on the game layer, and map meshes get their own layer excluded from entity raycasts.
5. Spawn placement asks `ISpawnSafetyService` before activating an entity (§7).

---

## 7. Safety-layer architecture for spawn filtering

Goal: never place mobs/NPCs (or encourage walking) onto roads, rail, water, inside buildings, or into private/inaccessible/sensitive areas.

**Principle: the safety data is provider-independent and OSM-based.** Even if Mapbox were the renderer, the safety layer would still come from OSM, because Google forbids machine interpretation of its tiles and Mapbox analysis rights are UNVERIFIED.

### 7.1 Pipeline (offline/server-side build, not on the phone)

```
Geofabrik extract (e.g. poland-latest.osm.pbf, ODbL)
  → filter/buffer step (osmium / Planetiler custom profile / PostGIS — tool choice in a later issue)
  → "spawn safety" dataset in our own schema, e.g.:
        layer blocked   : polygons (buffered) + class
        layer allowed   : optional positive areas (parks, pedestrian squares)
  → packaged as PMTiles (vector) or as an S2/H3 cell set per region
  → versioned, published next to the base map tiles
```

Candidate OSM inputs (owner must confirm the category list and buffers, see §9):

| Category | Example OSM tags | Default handling |
|---|---|---|
| Motor roads | `highway=motorway/trunk/primary/secondary/tertiary/residential/service` (+ `*_link`) | Block, with a buffer that depends on road class |
| Rail / tram | `railway=rail/tram/light_rail/subway` | Block + buffer |
| Water | `natural=water`, `waterway=*`, `natural=coastline`, `landuse=reservoir` | Block + buffer |
| Buildings | `building=*` | Block (footprint + small buffer) |
| Private / no access | `access=private/no` on ways/areas | Block |
| Sensitive land use | e.g. `landuse=military/industrial/construction/railway`, `aeroway=*`, `amenity=school/kindergarten/hospital`, `landuse=cemetery`, `amenity=place_of_worship` | Block or owner-chosen policy |
| Positive areas | `leisure=park/playground` (not at night?), `highway=pedestrian`, `place=square` | Allowed (still subject to blocks) |

Tag semantics come from the OSM wiki (`Key:access`: `private`/`no`). The exact tag list is a **proposal** and needs owner approval.

### 7.2 Runtime contract

```csharp
// MetinAR.Map.Abstractions (proposal)
enum SafetyVerdict { Allowed, Blocked, Unknown }
struct SafetyResult { SafetyVerdict Verdict; string ReasonClass; string DataVersion; }
interface ISpawnSafetyService
{
    SafetyResult Evaluate(GeoCoordinate point, double clearanceMeters);
    bool TryFindSafePoint(GeoCoordinate near, double searchRadiusMeters, out GeoCoordinate safe);
}
```

- **Default-deny:** `Unknown` (no data loaded, outside coverage, stale data) is treated as `Blocked` for spawns.
- **GPS uncertainty:** clearance ≥ reported horizontal accuracy. Reuse the confidence idea of `AggroConfidenceGate` (METINAR-005A) so nothing spawns when the fix is poor.
- **Authority:** for the prototype the client evaluates. In production the **server** should decide spawns (anti-cheat, consistent across players), and the client only renders.
- OSM data is incomplete (missing private gardens, unmapped fences). So: conservative buffers, a player "report unsafe spawn" action, and a server-side blocklist overlay.
- **ODbL:** a publicly used safety dataset derived from OSM is a **Derivative Database**, so it must be offered under ODbL (share-alike) if used publicly. Game content (mob tables, quests) should be kept as a separate database that only references locations, to stay a "Collective Database". **Legal interpretation is UNVERIFIED and needs owner/legal confirmation.**

---

## 8. Proof-of-concept plan (proposed follow-up issue, e.g. METINAR-008B)

Starts **only after** the owner approves §9 Q1–Q2. No accounts, keys, billing or paid assets. Its own branch. A **new** isolated scene (`WorldMapOsmPoc.unity`), so the existing `WorldMapPrototype.unity`, the AR scenes and the GPS core stay untouched.

| Step | Work | Gate |
|---|---|---|
| P1 | Pure `TileMath` (lon/lat ↔ z/x/y, tile bounds → local meters via `WorldMapOrigin` math) | `dotnet test tests/GeoCore.Tests` new tests pass. Existing 81 stay green. |
| P2 | `IMapTileSource` for OpenFreeMap TileJSON + HTTP with unique User-Agent, cache-header-respecting disk cache. **No tiles committed to git.** | Unit tests for URL/cache logic. Manual Editor fetch of a 3×3 tile area. |
| P3 | MVT decode (`vector-tile-cs`, BSD-3, vendored with licence) → `MapFeature` | Tests with a small **self-generated** fixture (not copied provider tiles). Otherwise fixture licensing is noted. |
| P4 | Mesh builder: water/landuse polygons (earcut), road ribbons by class, extruded buildings. One `MapStyleProfile` with 3–5 URP materials. | Unity 6000.6.4f1 batch compile exit 0, 0 `error CS`. |
| P5 | Overlay existing player/mob/NPC prefabs using `WorldMapEntity` on the new map. Tilt/rotate via the camera. | Play Mode smoke: entities at expected local positions over map features. |
| P6 | Attribution HUD element ("© OpenStreetMap contributors", "© OpenMapTiles", OpenFreeMap) + licences panel | Smoke test asserts attribution visible. |
| P7 | Android dev APK on LuzMen. Measure APK delta vs 51.1 MB, tile bytes per 3×3 area, decode/mesh ms, draw calls, FPS on the owner's phone. | Build exit 0. Manifest still has no `ACCESS_BACKGROUND_LOCATION`. **Device numbers UNVERIFIED until run on the phone.** |
| P8 (time-boxed) | Same 3×3 area via `maplibre-unity` in a throwaway branch, Android only | Report: works on device yes/no, APK delta, FPS. No merge. |
| P9 | Stub `ISpawnSafetyService` using the same tile's `building`/`water`/`transportation` layers (client-side, prototype only) | Tests: point on road / in water / in building → Blocked; open park → Allowed; no data → Unknown→Blocked. |

POC exit criteria: stable ≥30 FPS on the owner's phone in a dense Polish city block (target, not yet verified), APK delta ≤ ~5 MB (target), no provider type referenced outside `MetinAR.Map.*`.

---

## 9. Explicit owner decisions required before integration

The issue should be `[BLOCKED — OWNER DECISION]` after technical review until these are answered:

1. **Provider:** approve **OSM vector tiles + own Unity renderer** as the first choice (and Mapbox Unity only as a fallback)? Yes/No.
2. **Prototype tile host:** OK to use the **OpenFreeMap public instance** (free, no account, no SLA, Cloudflare CDN) for development/prototype builds? Or go straight to self-hosting?
3. **Production hosting budget/scope:** which launch region (Poland only vs. EU vs. worldwide) and which hosting provider/budget for self-hosted PMTiles? (Cost **UNVERIFIED** until quoted.)
4. **ODbL obligations:** accept in-game OSM attribution (HUD corner + licences screen) and ODbL share-alike for any publicly used OSM-derived database (e.g. the spawn-safety layer)? Get legal confirmation?
5. **Mapbox fallback:** if the POC fails, may we contact Mapbox Sales and create an account/token (needs owner credentials, so the owner does it)?
6. **Google / Cesium photoreal:** confirm they're **out of scope** for the GPS map (EEA 403, no restyling, no caching, non-commercial ion tier).
7. **Safety policy:** approve the blocked category list and buffers in §7.1 (especially schools, cemeteries, places of worship, hospitals, private land) and **default-deny** for unknown areas.
8. **Spawn authority:** OK that production spawns are decided server-side (needs a backend issue later)?
9. **Assembly boundaries:** introduce asmdefs to enforce the map ↔ AR boundary (§6.2) in a separate chore issue?
10. **POC go-ahead:** create follow-up issue METINAR-008B per §8.

---

## 10. What was not verified

- No candidate was installed, compiled or run. **All Unity 6000.6.4f1 compatibility is from vendor docs**, and none lists 6.6 explicitly.
- Mapbox Unity pricing (sales-only), Unity-specific free tier, offline/caching terms, exact attribution text, and whether Mapbox data may be analysed for spawn safety: **UNVERIFIED**.
- ArcGIS Unity pricing and game-use terms: **UNVERIFIED**.
- APK size deltas and runtime performance for every option: **UNVERIFIED** (needs P7/P8).
- Hosting/egress cost for self-hosted PMTiles: **UNVERIFIED** (needs owner-chosen host quote).
- Terrain data licensing (e.g. Mapterhorn): **UNVERIFIED**.
- ODbL interpretation for game databases: **UNVERIFIED** (legal review).
- 8th Wall: not evaluated.

---

## Sources

All accessed **2026-10-20**.

Repository context
- `docs/AUTONOMOUS_WORKFLOW.md`, `docs/METINAR-004.md`, `client/Packages/manifest.json`, `client/Assets/MetinAR/Scripts/WorldMap/*.cs`, `Scripts/ArEncounter/ArEncounterHandoff.cs` (main @ 8557a3e)

OpenStreetMap / OSM-derived
- OSMF Tile Usage Policy: https://operations.osmfoundation.org/policies/tiles/
- OSMF Vector Tile Usage Policy: https://operations.osmfoundation.org/policies/vector/
- OSMF Attribution Guideline (adopted 2021-06-25): https://osmfoundation.org/wiki/Licence/Attribution_Guidelines
- OSM wiki Key:access: https://wiki.openstreetmap.org/wiki/Key:access
- Geofabrik Poland extract: https://download.geofabrik.de/europe/poland.html
- OpenFreeMap site/FAQ: https://openfreemap.org/ and TOS (updated 2026-10-14): https://openfreemap.org/tos/
- OpenFreeMap repo: https://github.com/hyperknot/openfreemap
- OpenMapTiles licence: https://github.com/openmaptiles/openmaptiles/blob/master/LICENSE.md
- Protomaps basemap downloads: https://docs.protomaps.com/basemaps/downloads ; licence: https://github.com/protomaps/basemaps/blob/main/LICENSE.md
- PMTiles licence: https://github.com/protomaps/PMTiles/blob/main/LICENSE
- Planetiler: https://github.com/onthegomap/planetiler
- MapLibre Native: https://github.com/maplibre/maplibre-native
- maplibre-unity (community): https://github.com/KazukiKuriyama/maplibre-unity (README, releases)
- vector-tile-cs: https://github.com/mapbox/vector-tile-cs ; earcut: https://github.com/mapbox/earcut

Mapbox
- Unity SDK README/tags/changelog/migration: https://github.com/mapbox/mapbox-unity-sdk (README, `/tags`, `CHANGELOG.md`, `Documentation~/Migration-3.0-to-3.1.md`, LICENSE)
- Issue #1980 (GA status, Mar 2026): https://github.com/mapbox/mapbox-unity-sdk/issues/1980
- Unity pricing: https://docs.mapbox.com/unity/guides/pricing/
- Unity applications guide: https://docs.mapbox.com/help/dive-deeper/unity/
- Sign-up / trial limits: https://docs.mapbox.com/accounts/guides/signup/
- Pricing: https://www.mapbox.com/pricing
- Privacy FAQ (via product terms page): https://www.mapbox.com/legal/service-terms

Google
- Deprecations (gaming services decommissioned): https://developers.google.com/maps/deprecations
- Gaming solution blog (deprecation note): https://mapsplatform.google.com/resources/blog/google-maps-platform-gaming-services-now-available-everyone/
- Map Tiles API EEA adjustments: https://developers.google.com/maps/comms/eea/map-tiles
- Map Tiles usage and billing: https://developers.google.com/maps/documentation/tile/usage-and-billing
- Map Tiles policies: https://developers.google.com/maps/documentation/tile/policies
- Pricing list: https://developers.google.com/maps/billing-and-pricing/pricing
- 3D Tiles renderer (Cesium for Unity): https://developers.google.com/maps/documentation/tile/use-renderer
- Geospatial Creator: https://developers.google.com/ar/geospatialcreator

Cesium
- Releases: https://github.com/CesiumGS/cesium-unity/releases ; changelog: https://cesium.com/learn/cesium-unity/ref-doc/changes.html
- Supported platforms: https://cesium.com/learn/cesium-unity/ref-doc/supported-platforms.html
- Cesium ion pricing: https://cesium.com/pricing/

Esri
- ArcGIS Maps SDK for Unity 2.4 system requirements: https://developers.arcgis.com/unity/system-requirements/system-requirements-2-4-0/
- Downloads / release notes (search results): https://developers.arcgis.com/unity/downloads/ , https://developers.arcgis.com/unity/release-notes/release-notes-2-4-0/

Niantic
- Maps SDK sunset notice: https://www.nianticspatial.com/products/maps-sdk-notice
- NSDK migration guide: https://www.nianticspatial.com/docs/nsdk/migration_guide/
- NSDK 3.17 overview: https://www.nianticspatial.com/docs/nsdk/3.17.0/

Unity
- Unity 6.6 availability (6000.6 GA 2026-09-01; latest 6000.6.6f1 2026-10-15, search result): https://discussions.unity.com/t/unity-6-6-is-now-available/1735357
