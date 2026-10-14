using MetinAR.WorldMap;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MetinAR.UI
{
    /// <summary>
    /// Mob details panel with live distance and AR encounter button.
    /// </summary>
    public sealed class MobInfoPanel : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _nameText;

        [SerializeField]
        private TextMeshProUGUI _levelText;

        [SerializeField]
        private TextMeshProUGUI _hpText;

        [SerializeField]
        private TextMeshProUGUI _distanceText;

        [SerializeField]
        private TextMeshProUGUI _aggressiveText;

        [SerializeField]
        private Button _enterArButton;

        [SerializeField]
        private Button _closeButton;

        [SerializeField]
        private WorldMapPrototypeController _controller;

        private PrototypeMob _mob;

        public Button EnterArButton => _enterArButton;
        public Button CloseButton => _closeButton;
        public string NameLabel => _nameText != null ? _nameText.text : null;
        public string LevelLabel => _levelText != null ? _levelText.text : null;
        public string HpLabel => _hpText != null ? _hpText.text : null;
        public string DistanceLabel => _distanceText != null ? _distanceText.text : null;
        public string AggressiveLabel => _aggressiveText != null ? _aggressiveText.text : null;

        public void Configure(
            TextMeshProUGUI nameText,
            TextMeshProUGUI levelText,
            TextMeshProUGUI hpText,
            TextMeshProUGUI distanceText,
            TextMeshProUGUI aggressiveText,
            Button enterArButton,
            Button closeButton,
            WorldMapPrototypeController controller)
        {
            _nameText = nameText;
            _levelText = levelText;
            _hpText = hpText;
            _distanceText = distanceText;
            _aggressiveText = aggressiveText;
            _enterArButton = enterArButton;
            _closeButton = closeButton;
            _controller = controller;
            WireButtons();
        }

        private void Awake()
        {
            WireButtons();
        }

        private void WireButtons()
        {
            if (_enterArButton != null)
            {
                _enterArButton.onClick.RemoveListener(OnEnterArClicked);
                _enterArButton.onClick.AddListener(OnEnterArClicked);
            }

            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(Hide);
                _closeButton.onClick.AddListener(Hide);
            }
        }

        private void Update()
        {
            if (!gameObject.activeInHierarchy || _mob == null)
            {
                return;
            }

            RefreshDistance();
        }

        public void Show(PrototypeMob mob)
        {
            _mob = mob;
            gameObject.SetActive(true);
            RefreshStatic();
            RefreshDistance();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            _mob = null;
        }

        private void RefreshStatic()
        {
            if (_mob == null)
            {
                return;
            }

            if (_nameText != null)
            {
                _nameText.text = _mob.MobName;
            }

            if (_levelText != null)
            {
                _levelText.text = "Poziom " + _mob.Level;
            }

            if (_hpText != null)
            {
                _hpText.text = "HP " + _mob.CurrentHp + "/" + _mob.MaxHp;
            }

            if (_aggressiveText != null)
            {
                _aggressiveText.text = _mob.Aggressive ? "Agresywny: tak" : "Agresywny: nie";
            }
        }

        private void RefreshDistance()
        {
            if (_distanceText == null || _mob == null)
            {
                return;
            }

            float d = _mob.DistanceToPlayerMeters;
            _distanceText.text = "Dystans: " + d.ToString("F1") + " m";
        }

        private void OnEnterArClicked()
        {
            string reason = _mob != null ? "panel:" + _mob.MobName : "panel";
            _controller?.RequestArEncounter(reason);
        }
    }
}
