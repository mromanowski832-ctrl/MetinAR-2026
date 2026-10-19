using MetinAR.WorldMap;
using UnityEngine;
using UnityEngine.UI;

namespace MetinAR.UI
{
    /// <summary>
    /// Bottom HUD buttons: EQ / Skille / Ustawienia / Postać / Hamburger.
    /// </summary>
    public sealed class HudButtonBar : MonoBehaviour
    {
        [SerializeField]
        private Button _eqButton;

        [SerializeField]
        private Button _skillsButton;

        [SerializeField]
        private Button _settingsButton;

        [SerializeField]
        private Button _characterButton;

        [SerializeField]
        private Button _hamburgerButton;

        [SerializeField]
        private WorldMapPrototypeController _controller;

        public Button EqButton => _eqButton;
        public Button SkillsButton => _skillsButton;
        public Button SettingsButton => _settingsButton;
        public Button CharacterButton => _characterButton;
        public Button HamburgerButton => _hamburgerButton;

        public void Configure(
            Button eqButton,
            Button skillsButton,
            Button settingsButton,
            Button characterButton,
            Button hamburgerButton,
            WorldMapPrototypeController controller)
        {
            _eqButton = eqButton;
            _skillsButton = skillsButton;
            _settingsButton = settingsButton;
            _characterButton = characterButton;
            _hamburgerButton = hamburgerButton;
            _controller = controller;
            Wire();
        }

        private void Awake()
        {
            Wire();
        }

        private void Wire()
        {
            Bind(_eqButton, () => _controller?.TogglePanel(_controller.InventoryPanel != null ? _controller.InventoryPanel.gameObject : null));
            Bind(_skillsButton, () => _controller?.TogglePanel(_controller.SkillsPanel != null ? _controller.SkillsPanel.gameObject : null));
            Bind(_settingsButton, () => _controller?.TogglePanel(_controller.SettingsPanel != null ? _controller.SettingsPanel.gameObject : null));
            Bind(_characterButton, () => _controller?.TogglePanel(_controller.CharacterPanel != null ? _controller.CharacterPanel.gameObject : null));
            Bind(_hamburgerButton, () => _controller?.TogglePanel(_controller.HamburgerPanel != null ? _controller.HamburgerPanel.gameObject : null));
        }

        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }
    }
}
