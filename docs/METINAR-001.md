# METINAR-001 — Geolocation core (`GeoCoordinate` / `GeoMath`)

## What was implemented

Pure C# (no `UnityEngine`) types under `MetinAR.Location`:

| File | Role |
|------|------|
| `client/Assets/MetinAR/Scripts/Location/GeoCoordinate.cs` | Immutable `readonly struct` holding latitude/longitude with range validation |
| `client/Assets/MetinAR/Scripts/Location/GeoMath.cs` | Static helpers: haversine distance and local tangent-plane meter offsets |

Branch: `feat/001-geo-core` (from `main`). Main was not modified.

## Coordinate format

- **Units:** decimal degrees
- **Datum / frame:** WGS84 geographic coordinates as typically reported by consumer GPS
- **Latitude:** \([-90, 90]\) inclusive (north positive)
- **Longitude:** \([-180, 180]\) inclusive (east positive)
- **Altitude:** not modeled
- Invalid constructor arguments throw `ArgumentOutOfRangeException`
- **NaN, positive infinity, and negative infinity are rejected** for latitude and longitude

## Distance formula and Earth model

`GeoMath.DistanceMeters` uses the **haversine** formula on a **sphere**, not a full Vincenty / WGS84 ellipsoid.

Named constant:

```text
EarthRadiusMeters = 6371008.8
```

This is the IUGG mean Earth radius commonly used for spherical distance approximations. It is not described here as both an authalic and a volumetric mean. Appropriate for short real-world GPS separations (tens to thousands of meters). Return value is meters. Identical inputs return exactly `0`.

Before the square roots and `atan2`, the haversine intermediate value is clamped into `[0.0, 1.0]`. This is numeric robustness only. It uses plain C# comparisons, not `UnityEngine.Mathf`.

Related named scale:

```text
MetersPerDegreeLatitude = EarthRadiusMeters * π / 180
```

(~111195.08 m per degree of latitude on this sphere).

## OffsetMeters

Local tangent-plane offset from an origin:

- `northMeters` positive → north; `eastMeters` positive → east
- `deltaLatitudeDegrees = northMeters / MetersPerDegreeLatitude`
- `deltaLongitudeDegrees = eastMeters / (MetersPerDegreeLatitude * cos(latitudeRadians))`
- Longitude is normalized into \([-180, 180]\). Both -180 and +180 are valid; the range is closed, not closed-open
- `northMeters` and `eastMeters` must be finite. NaN and infinities are rejected before any calculation
- Near the poles, if `|cos(latitude)| < 1e-10`, throws `ArgumentOutOfRangeException` (avoids dividing by ~0)
- If the resulting latitude would leave \([-90, 90]\), throws `ArgumentOutOfRangeException`

## Expected GPS / model limitations

- Consumer GPS horizontal error is often **several meters**; these helpers do not fuse, filter, or estimate that error
- **No altitude** / vertical component
- **Sphere, not ellipsoid** — fine for short AR/GPS use; not survey-grade
- Intended for **short distances** (tens–thousands of meters), not continental geodesy
- **Poles:** east offsets are rejected when cosine of latitude is tiny
- Deterministic, no external deps, no `unsafe`, no threading; suitable for mobile clients

## What has NOT been tested yet

**Compilation is still UNVERIFIED** because no C# compiler is available on the agent machine.

No usable C# / .NET compiler was found (`dotnet`, `csc`, and `mcs` were all absent). Nothing was installed. Therefore:

- Sources were **not** compiled
- Automated tests were **not** run
- Do **not** treat this work as a green build

### Planned checks (not run)

1. Same-coordinate distance ≈ 0 m (tolerance ≪ 0.001 m; exact 0 preferred)
2. Known short distance: Warsaw-ish `(52.2297, 21.0122)`, `OffsetMeters(..., 50, 0)` then `DistanceMeters` ≈ 50 m (tolerance ~0.05 m); and ~1° latitude delta ≈ `EarthRadiusMeters * π/180` within ~0.01% relative
3. Pure north offset increases latitude; longitude unchanged within epsilon
4. Pure east offset in the northern hemisphere increases longitude; latitude unchanged
5. Invalid latitudes `91` and `-91` rejected
6. Invalid longitudes `181` and `-181` rejected

## Runtime note

Compiler check result: no `dotnet` / `csc` / `mcs` on PATH. Version string: **none (UNVERIFIED)**.
