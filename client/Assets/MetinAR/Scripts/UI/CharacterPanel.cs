using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MetinAR.UI
{
    /// <summary>
    /// Character preview + stats + skills placeholder areas.
    /// </summary>
    public sealed class CharacterPanel : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _statsText;

        [SerializeField]
        private TextMeshProUGUI _skillsText;

        [SerializeField]
        private Button _closeButton;

        public void Configure(TextMeshProUGUI statsText, TextMeshProUGUI skillsText, Button closeButton)
        {
            _statsText = statsText;
            _skillsText = skillsText;
            _closeButton = closeButton;
            Wire();
            ApplyDefaults();
        }

        private void Awake()
        {
            Wire();
            ApplyDefaults();
        }

        private void Wire()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(Hide);
                _closeButton.onClick.AddListener(Hide);
            }
        }

        private void ApplyDefaults()
        {
            if (_statsText != null && string.IsNullOrEmpty(_statsText.text))
            {
                _statsText.text = "Siła 12\nŻywotność 14\nInteligencja 10\nZręczność 11";
            }

            if (_skillsText != null && string.IsNullOrEmpty(_skillsText.text))
            {
                _skillsText.text = "Umiejętności (prototyp)\n• Cios\n• Blok\n• Sprint";
            }
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
