using MetinAR.Gameplay.Combat;
using MetinAR.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MetinAR.UI
{
    /// <summary>
    /// Top/bottom world-map HUD chrome: portrait, stats, radar, quest, coin, EXP, button bar.
    /// METINAR-020: level, the 4 × 25% EXP orbs (fractional fill, CHARACTER_PROGRESSION.md §2.1) and the talary
    /// count come from <see cref="LocalPlayerSession"/>; HP/MP/quest stay prototype values until S3.
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

        /// <summary>Use the local player progress for level, EXP orbs and talary (false: serialized prototype values).</summary>
        [SerializeField]
        private bool _bindToLocalPlayer = true;

        /// <summary>Talary count next to the coin; created at runtime on the coin when not assigned.</summary>
        [SerializeField]
        private TextMeshProUGUI _talaryText;

        private float _filledExpOrbs;

        /// <summary>Filled EXP orbs 0…4 currently shown (fractional).</summary>
        public float FilledExpOrbs => _filledExpOrbs;

        public string LevelLabel => _levelText != null ? _levelText.text : null;

        public string TalaryLabel => _talaryText != null ? _talaryText.text : null;

        public Image[] ExpOrbs => _expSegments;

        public bool BindToLocalPlayer
        {
            get => _bindToLocalPlayer;
            set
            {
                _bindToLocalPlayer = value;
                Refresh();
            }
        }

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

        private void OnEnable()
        {
            LocalPlayerSession.Changed -= Refresh;
            LocalPlayerSession.Changed += Refresh;
        }

        private void OnDisable()
        {
            LocalPlayerSession.Changed -= Refresh;
        }

        private void Start()
        {
            EnsureTalaryText();
            Refresh();
        }

        private void EnsureTalaryText()
        {
            if (_talaryText != null || _coinIcon == null || _coinIcon.parent == null)
            {
                return;
            }

            // On the (non-rotating) coin rim, so the number stays upright while the coin spins.
            var go = new GameObject("TalaryText", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_coinIcon.parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _talaryText = go.AddComponent<TextMeshProUGUI>();
            if (_levelText != null && _levelText.font != null)
            {
                _talaryText.font = _levelText.font;
            }

            _talaryText.fontSize = 24;
            _talaryText.fontStyle = FontStyles.Bold;
            _talaryText.alignment = TextAlignmentOptions.Center;
            _talaryText.color = new Color(0.25f, 0.16f, 0.02f);
            _talaryText.raycastTarget = false;
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

            int level = _level;
            _filledExpOrbs = _filledExpSegments;
            long talary = 0;
            if (_bindToLocalPlayer)
            {
                PlayerProgress progress = LocalPlayerSession.Progress;
                level = progress.Level;
                _filledExpOrbs = (float)progress.FilledHudOrbs;
                talary = progress.Talary;
            }

            if (_levelText != null)
            {
                _levelText.text = "Lv " + level;
            }

            if (_talaryText != null)
            {
                _talaryText.text = talary.ToString();
                _talaryText.gameObject.SetActive(_bindToLocalPlayer);
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

                    // Each orb is 25% of the level; a partly filled orb fills from the bottom (fractional, §2.1).
                    Image orb = _expSegments[i];
                    orb.type = Image.Type.Filled;
                    orb.fillMethod = Image.FillMethod.Vertical;
                    orb.fillOrigin = (int)Image.OriginVertical.Bottom;
                    orb.fillAmount = (float)HudExpOrbs.OrbFill(_filledExpOrbs, i);
                    Color c = orb.color;
                    c.a = 1f;
                    orb.color = c;
                }
            }
        }
    }
}
