using System;
using MetinAR.Location;
using UnityEngine;

namespace MetinAR.WorldMap
{
    /// <summary>
    /// Prototype-only placement: waits for the player's first valid <see cref="LocationFix"/>,
    /// captures the shared <see cref="WorldMapOrigin"/> from that exact coordinate, and places the
    /// test mob / NPC relative to it with <see cref="GeoMath.OffsetMeters"/>. Initializes once.
    /// Works with any provider (mock in Editor, Unity GPS on device). No hard-coded real coordinates.
    /// Runs before other world-map scripts so avatar/entities see the origin in the same frame.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PrototypeWorldPlacement : MonoBehaviour
    {
        public const double DefaultMobNorthMeters = 13.0;
        public const double DefaultMobEastMeters = 2.0;
        public const double DefaultNpcNorthMeters = 4.0;
        public const double DefaultNpcEastMeters = -2.5;

        [SerializeField]
        private PlayerLocationService _locationService;

        [SerializeField]
        private WorldMapOrigin _origin;

        [SerializeField]
        private WorldMapEntity _mob;

        [SerializeField]
        private WorldMapEntity _npc;

        [SerializeField]
        private double _mobNorthMeters = DefaultMobNorthMeters;

        [SerializeField]
        private double _mobEastMeters = DefaultMobEastMeters;

        [SerializeField]
        private double _npcNorthMeters = DefaultNpcNorthMeters;

        [SerializeField]
        private double _npcEastMeters = DefaultNpcEastMeters;

        private GeoCoordinate _firstFix;
        private GeoCoordinate _mobCoordinate;
        private GeoCoordinate _npcCoordinate;

        /// <summary>Raised once after origin capture and entity placement.</summary>
        public event Action<PrototypeWorldPlacement> Initialized;

        public bool IsInitialized { get; private set; }
        public GeoCoordinate FirstFixCoordinate => _firstFix;
        public GeoCoordinate MobCoordinate => _mobCoordinate;
        public GeoCoordinate NpcCoordinate => _npcCoordinate;
        public double MobNorthMeters => _mobNorthMeters;
        public double MobEastMeters => _mobEastMeters;
        public double NpcNorthMeters => _npcNorthMeters;
        public double NpcEastMeters => _npcEastMeters;
        public WorldMapOrigin Origin => _origin;

        public void Configure(
            PlayerLocationService locationService,
            WorldMapOrigin origin,
            WorldMapEntity mob,
            WorldMapEntity npc)
        {
            _locationService = locationService;
            _origin = origin;
            _mob = mob;
            _npc = npc;
        }

        private void Update()
        {
            TryInitialize();
        }

        /// <summary>
        /// Attempts one-time initialization from the latest fix. Returns true once initialized.
        /// </summary>
        public bool TryInitialize()
        {
            if (IsInitialized)
            {
                return true;
            }

            if (_locationService == null || _origin == null)
            {
                return false;
            }

            if (!_locationService.TryGetLatestFix(out LocationFix fix))
            {
                return false;
            }

            _firstFix = fix.Coordinate;
            _origin.CaptureOrigin(_firstFix);
            // If something else captured the origin earlier, place relative to that shared origin.
            GeoCoordinate anchor = _origin.Origin;

            _mobCoordinate = GeoMath.OffsetMeters(anchor, _mobNorthMeters, _mobEastMeters);
            _npcCoordinate = GeoMath.OffsetMeters(anchor, _npcNorthMeters, _npcEastMeters);
            Place(_mob, _mobCoordinate);
            Place(_npc, _npcCoordinate);

            IsInitialized = true;
            Initialized?.Invoke(this);
            return true;
        }

        private static void Place(WorldMapEntity entity, GeoCoordinate coordinate)
        {
            if (entity == null)
            {
                return;
            }

            entity.SetCoordinate(coordinate);
            if (!entity.gameObject.activeSelf)
            {
                entity.gameObject.SetActive(true);
            }
        }
    }
}
