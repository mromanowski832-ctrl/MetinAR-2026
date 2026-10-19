using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MetinAR.UI
{
    /// <summary>
    /// Top/bottom world-map HUD chrome: portrait, stats, radar, quest, coin, EXP, button bar.
    /// </summary>
    public sealed class WorldMapHud : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _nicknameText;

        [SerializeField]
        private TextMeshProUGUI _levelText;

        [SerializeField]
        private TextMeshProUGUI _hpText;

        [SerializeField]
        private TextMeshProUGUI _mpText;

        [SerializeField]
        private Image _hpFill;

        [SerializeField]
        private Image _mpFill;

        [SerializeField]
        private TextMeshProUGUI _questText;

        [SerializeField]
        private RectTransform _coinIcon;

        [SerializeField]
        private Image[] _expSegments = new Image[4];

        [SerializeField]
        private float _coinSpinDegreesPerSecond = 45f;

        [SerializeField]
        private string _nickname = "Wędrowiec";

        [SerializeField]
        private int _level = 12;

        [SerializeField]
        private int _hp = 420;

        [SerializeField]
        private int _maxHp = 500;

        [SerializeField]
        private int _mp = 180;

        [SerializeField]
        private int _maxMp = 200;

        [SerializeField]
        private int _questProgress = 2;

        [SerializeField]
        private int _questTarget = 5;

        [SerializeField]
        [Range(0, 4)]
        private int _filledExpSegments = 2;

        public void Configure(
            TextMeshProUGUI nicknameText,
            TextMeshProUGUI levelText,
            TextMeshProUGUI hpText,
            TextMeshProUGUI mpText,
            Image hpFill,
            Image mpFill,
            TextMeshProUGUI questText,
            RectTransform coinIcon,
            Image[] expSegments)
        {
            _nicknameText = nicknameText;
            _levelText = levelText;
            _hpText = hpText;
            _mpText = mpText;
            _hpFill = hpFill;
            _mpFill = mpFill;
            _questText = questText;
            _coinIcon = coinIcon;
            _expSegments = expSegments;
            Refresh();
        }

        private void Start()
        {
            Refresh();
        }

        private void Update()
        {
            if (_coinIcon != null)
            {
                _coinIcon.Rotate(0f, 0f, -_coinSpinDegreesPerSecond * Time.deltaTime);
            }
        }

        public void Refresh()
        {
            if (_nicknameText != null)
            {
                _nicknameText.text = _nickname;
            }

            if (_levelText != null)
            {
                _levelText.text = "Lv " + _level;
            }

            if (_hpText != null)
            {
                _hpText.text = "HP " + _hp + "/" + _maxHp;
            }

            if (_mpText != null)
            {
                _mpText.text = "MP " + _mp + "/" + _maxMp;
            }

            if (_hpFill != null)
            {
                _hpFill.fillAmount = _maxHp > 0 ? (float)_hp / _maxHp : 0f;
            }

            if (_mpFill != null)
            {
                _mpFill.fillAmount = _maxMp > 0 ? (float)_mp / _maxMp : 0f;
            }

            if (_questText != null)
            {
                _questText.text = "Zabij 5 pająków: " + _questProgress + "/" + _questTarget;
            }

            if (_expSegments != null)
            {
                for (int i = 0; i < _expSegments.Length; i++)
                {
                    if (_expSegments[i] == null)
                    {
                        continue;
                    }

                    Color c = _expSegments[i].color;
                    c.a = i < _filledExpSegments ? 1f : 0.25f;
                    _expSegments[i].color = c;
                }
            }
        }
    }
}
