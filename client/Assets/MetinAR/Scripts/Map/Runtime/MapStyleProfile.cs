using UnityEngine;

namespace MetinAR.Map.Runtime
{
    /// <summary>
    /// Art-facing style for the OSM POC: one URP material per <see cref="MapRenderLayer"/> plus the few
    /// geometry knobs of <see cref="MapMeshStyle"/>. Kept separate from the renderer so art can iterate
    /// without touching code.
    /// </summary>
    [CreateAssetMenu(menuName = "MetinAR/Map/OSM Map Style", fileName = "OsmMapStyle")]
    public sealed class MapStyleProfile : ScriptableObject
    {
        [SerializeField] private Material _landcover;
        [SerializeField] private Material _landuse;
        [SerializeField] private Material _park;
        [SerializeField] private Material _water;
        [SerializeField] private Material _roadMinor;
        [SerializeField] private Material _roadMajor;
        [SerializeField] private Material _path;
        [SerializeField] private Material _rail;
        [SerializeField] private Material _building;

        [SerializeField, Range(0.1f, 2f)] private float _buildingHeightScale = 0.6f;
        [SerializeField, Range(4f, 120f)] private float _maxBuildingHeight = 30f;
        [SerializeField] private bool _buildingsCastShadows;

        public bool BuildingsCastShadows => _buildingsCastShadows;

        public void Configure(Material landcover, Material landuse, Material park, Material water, Material roadMinor,
            Material roadMajor, Material path, Material rail, Material building)
        {
            _landcover = landcover;
            _landuse = landuse;
            _park = park;
            _water = water;
            _roadMinor = roadMinor;
            _roadMajor = roadMajor;
            _path = path;
            _rail = rail;
            _building = building;
        }

        public Material MaterialFor(MapRenderLayer layer)
        {
            switch (layer)
            {
                case MapRenderLayer.Landcover: return _landcover;
                case MapRenderLayer.Landuse: return _landuse;
                case MapRenderLayer.Park: return _park;
                case MapRenderLayer.Water: return _water;
                case MapRenderLayer.RoadMinor: return _roadMinor;
                case MapRenderLayer.RoadMajor: return _roadMajor;
                case MapRenderLayer.Path: return _path;
                case MapRenderLayer.Rail: return _rail;
                case MapRenderLayer.Building: return _building;
                default: return null;
            }
        }

        public MapMeshStyle CreateMeshStyle()
        {
            return new MapMeshStyle
            {
                BuildingHeightScale = _buildingHeightScale,
                MaxBuildingHeight = _maxBuildingHeight
            };
        }
    }
}
