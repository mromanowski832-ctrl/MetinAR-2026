using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MetinAR.UI
{
    /// <summary>
    /// Generic titled placeholder panel (Skille / Ustawienia / Hamburger).
    /// </summary>
    public sealed class PlaceholderPanel : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _titleText;

        [SerializeField]
        private TextMeshProUGUI _bodyText;

        [SerializeField]
        private Button _closeButton;

        public void Configure(TextMeshProUGUI titleText, TextMeshProUGUI bodyText, Button closeButton, string title, string body)
        {
            _titleText = titleText;
            _bodyText = bodyText;
            _closeButton = closeButton;
            if (_titleText != null)
            {
                _titleText.text = title;
            }

            if (_bodyText != null)
            {
                _bodyText.text = body;
            }

            Wire();
        }

        private void Awake()
        {
            Wire();
        }

        private void Wire()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(Hide);
                _closeButton.onClick.AddListener(Hide);
            }
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
