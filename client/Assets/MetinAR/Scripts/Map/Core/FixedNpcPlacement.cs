using System;
using System.Collections.Generic;
using MetinAR.Location;

namespace MetinAR.Map
{
    /// <summary>A resolved fixed NPC site (kept in memory only; never logged or published).</summary>
    public readonly struct FixedNpcSite
    {
        public FixedNpcSite(string npcId, long cellRow, long cellColumn, GeoCoordinate coordinate, double distanceMeters, bool isOverride)
        {
            NpcId = npcId;
            CellRow = cellRow;
            CellColumn = cellColumn;
            Coordinate = coordinate;
            DistanceMeters = distanceMeters;
            IsOverride = isOverride;
        }

        public string NpcId { get; }

        public long CellRow { get; }

        public long CellColumn { get; }

        public GeoCoordinate Coordinate { get; }

        public double DistanceMeters { get; }

        /// <summary>True when the site came from a local (device-only) override instead of the grid.</summary>
        public bool IsOverride { get; }
    }

    /// <summary>
    /// METINAR-042 (#87) for #50: fixed ("stała") position of a shop NPC such as the Wędrowny Handlarz.
    /// Spec §2A: "NPC mogą mieć stałe lokalizacje". No coordinates are stored in the repo: the world is split
    /// into square-ish cells of <see cref="CellSizeMeters"/>, and each cell gets one deterministic site from a
    /// stable hash of (npc id, cell), so the NPC stands in the same place every session and on every device.
    /// When an <see cref="EncounterSitePolicy"/> is given, the site is moved (deterministically, same map data)
    /// to the nearest safe spot, or the cell has no NPC. Optional local overrides (e.g. an owner-chosen place
    /// stored only on the device) take precedence. Cell size is a prototype value awaiting owner decision.
    /// </summary>
    public sealed class FixedNpcPlacement
    {
        private readonly List<GeoCoordinate> _overrides = new List<GeoCoordinate>();

        public FixedNpcPlacement(string npcId, double cellSizeMeters = 500.0, double safeSearchRadiusMeters = 60.0)
        {
            if (string.IsNullOrWhiteSpace(npcId))
            {
                throw new ArgumentException("NPC id is required.", nameof(npcId));
            }

            if (!(cellSizeMeters >= 50.0) || double.IsInfinity(cellSizeMeters))
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeMeters), cellSizeMeters, "Cell size must be at least 50 m.");
            }

            NpcId = npcId;
            CellSizeMeters = cellSizeMeters;
            SafeSearchRadiusMeters = Math.Max(0, safeSearchRadiusMeters);
        }

        public string NpcId { get; }

        public double CellSizeMeters { get; }

        public double SafeSearchRadiusMeters { get; }

        /// <summary>Fraction of the cell kept free at each edge so neighbouring sites are never adjacent.</summary>
        public double EdgeMarginFraction { get; set; } = 0.2;

        /// <summary>Adds a device-local override site (never committed to the repo).</summary>
        public void AddLocalOverride(GeoCoordinate site)
        {
            _overrides.Add(site);
        }

        public void ClearLocalOverrides()
        {
            _overrides.Clear();
        }

        public void GetCell(GeoCoordinate p, out long row, out long column)
        {
            double cellLat = CellSizeMeters / GeoMath.MetersPerDegreeLatitude;
            row = (long)Math.Floor(p.Latitude / cellLat);
            double cellLon = CellLonDegrees(row, cellLat);
            column = (long)Math.Floor((p.Longitude + 180.0) / cellLon);
        }

        /// <summary>Deterministic, unadjusted site of this NPC in a cell.</summary>
        public GeoCoordinate NominalSite(long row, long column)
        {
            double cellLat = CellSizeMeters / GeoMath.MetersPerDegreeLatitude;
            double cellLon = CellLonDegrees(row, cellLat);
            ulong h = Hash(NpcId + "|" + row + "|" + column);
            double m = Math.Min(0.45, Math.Max(0, EdgeMarginFraction));
            double fy = m + (1 - 2 * m) * ((h & 0xFFFFFFFFUL) / 4294967296.0);
            double fx = m + (1 - 2 * m) * ((h >> 32) / 4294967296.0);
            double lat = (row + fy) * cellLat;
            double lon = (column + fx) * cellLon - 180.0;
            lat = Math.Max(-85.0, Math.Min(85.0, lat));
            if (lon > 180.0) lon -= 360.0;
            if (lon < -180.0) lon += 360.0;
            return new GeoCoordinate(lat, lon);
        }

        /// <summary>
        /// Nearest NPC site to <paramref name="player"/> among overrides and the player's cell plus its
        /// 8 neighbours. With a policy, unsafe grid sites are moved to the nearest safe spot or skipped.
        /// </summary>
        public bool TryResolveNearest(GeoCoordinate player, EncounterSitePolicy policy, out FixedNpcSite site)
        {
            site = default;
            bool found = false;
            double best = double.PositiveInfinity;

            foreach (GeoCoordinate o in _overrides)
            {
                double d = GeoMath.DistanceMeters(player, o);
                if (d < best)
                {
                    best = d;
                    site = new FixedNpcSite(NpcId, 0, 0, o, d, true);
                    found = true;
                }
            }

            GetCell(player, out long row, out long col);
            for (long dr = -1; dr <= 1; dr++)
            {
                for (long dc = -1; dc <= 1; dc++)
                {
                    GeoCoordinate c = NominalSite(row + dr, col + dc);
                    if (policy != null)
                    {
                        if (!policy.TryPlace(c, SafeSearchRadiusMeters, out GeoCoordinate safe, out _))
                        {
                            continue;
                        }

                        c = safe;
                    }

                    double d = GeoMath.DistanceMeters(player, c);
                    if (d < best)
                    {
                        best = d;
                        site = new FixedNpcSite(NpcId, row + dr, col + dc, c, d, false);
                        found = true;
                    }
                }
            }

            return found;
        }

        private double CellLonDegrees(long row, double cellLatDegrees)
        {
            double rowCentreLat = Math.Max(-85.0, Math.Min(85.0, (row + 0.5) * cellLatDegrees));
            double cos = Math.Cos(rowCentreLat * Math.PI / 180.0);
            return CellSizeMeters / (GeoMath.MetersPerDegreeLatitude * Math.Max(0.05, cos));
        }

        /// <summary>FNV-1a 64-bit: stable across runtimes (unlike string.GetHashCode).</summary>
        private static ulong Hash(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char ch in s)
            {
                h ^= ch;
                h *= 1099511628211UL;
            }

            // final avalanche so neighbouring cells do not get correlated offsets
            h ^= h >> 33;
            h *= 0xff51afd7ed558ccdUL;
            h ^= h >> 33;
            h *= 0xc4ceb9fe1a85ec53UL;
            h ^= h >> 33;
            return h;
        }
    }
}
