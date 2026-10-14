using MetinAR.UI;
using UnityEngine;

namespace MetinAR.WorldMap
{
    /// <summary>
    /// Prototype aggressive mob with world label, click → MobInfoPanel, and one-shot aggro toast.
    /// </summary>
    public sealed class PrototypeMob : MonoBehaviour
    {
        [SerializeField]
        private string _mobName = "Trujący Pająk";

        [SerializeField]
        private int _level = 5;

        [SerializeField]
        private int _maxHp = 120;

        [SerializeField]
        private int _currentHp = 120;

        [SerializeField]
        private bool _aggressive = true;

        [SerializeField]
        private float _aggroRangeMeters = 12f;

        [SerializeField]
        private Transform _player;

        [SerializeField]
        private WorldMapEntity _entity;

        [SerializeField]
        private MobInfoPanel _mobInfoPanel;

        [SerializeField]
        private WorldMapPrototypeController _controller;

        [SerializeField]
        private float _aggroDebounceSeconds = 8f;

        private bool _aggroTriggered;
        private float _nextAggroAllowedTime;

        public string MobName => _mobName;
        public int Level => _level;
        public int MaxHp => _maxHp;
        public int CurrentHp => _currentHp;
        public bool Aggressive => _aggressive;
        public float AggroRangeMeters => _aggroRangeMeters;

        public float DistanceToPlayerMeters
        {
            get
            {
                if (_player == null)
                {
                    return float.PositiveInfinity;
                }

                return Vector3.Distance(transform.position, _player.position);
            }
        }

        public void Configure(
            string mobName,
            int level,
            int maxHp,
            bool aggressive,
            float aggroRangeMeters,
            Transform player,
            WorldMapEntity entity,
            MobInfoPanel mobInfoPanel,
            WorldMapPrototypeController controller)
        {
            _mobName = mobName;
            _level = level;
            _maxHp = maxHp;
            _currentHp = maxHp;
            _aggressive = aggressive;
            _aggroRangeMeters = aggroRangeMeters;
            _player = player;
            _entity = entity;
            _mobInfoPanel = mobInfoPanel;
            _controller = controller;
            if (_entity != null)
            {
                _entity.DisplayName = _mobName;
            }
        }

        private void Update()
        {
            if (!_aggressive || _player == null)
            {
                return;
            }

            float distance = DistanceToPlayerMeters;
            if (distance > _aggroRangeMeters)
            {
                return;
            }

            if (_aggroTriggered && Time.unscaledTime < _nextAggroAllowedTime)
            {
                return;
            }

            _aggroTriggered = true;
            _nextAggroAllowedTime = Time.unscaledTime + _aggroDebounceSeconds;
            _controller?.NotifyAttacked(_mobName);
            _controller?.RequestArEncounter("aggro:" + _mobName);
        }

        /// <summary>
        /// Called by UI / collider adapters when the mob is selected.
        /// </summary>
        public void OpenInfoPanel()
        {
            if (_mobInfoPanel == null)
            {
                return;
            }

            _mobInfoPanel.Show(this);
            _controller?.ShowOnlyPanel(_mobInfoPanel.gameObject);
        }
    }
}
