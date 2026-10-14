using System.Collections;
using MetinAR.UI;
using TMPro;
using UnityEngine;

namespace MetinAR.WorldMap
{
    /// <summary>
    /// Orchestrates prototype HUD panels, AR-encounter toast, and bootstrap of missing refs.
    /// </summary>
    public sealed class WorldMapPrototypeController : MonoBehaviour
    {
        [SerializeField]
        private WorldMapHud _hud;

        [SerializeField]
        private MobInfoPanel _mobInfoPanel;

        [SerializeField]
        private InventoryPanel _inventoryPanel;

        [SerializeField]
        private CharacterPanel _characterPanel;

        [SerializeField]
        private PlaceholderPanel _skillsPanel;

        [SerializeField]
        private PlaceholderPanel _settingsPanel;

        [SerializeField]
        private PlaceholderPanel _hamburgerPanel;

        [SerializeField]
        private TextMeshProUGUI _toastText;

        [SerializeField]
        private GameObject _toastRoot;

        [SerializeField]
        private float _toastSeconds = 2.5f;

        private Coroutine _toastRoutine;
        private GameObject _activePanel;

        public MobInfoPanel MobInfoPanel => _mobInfoPanel;
        public InventoryPanel InventoryPanel => _inventoryPanel;
        public CharacterPanel CharacterPanel => _characterPanel;
        public PlaceholderPanel SkillsPanel => _skillsPanel;
        public PlaceholderPanel SettingsPanel => _settingsPanel;
        public PlaceholderPanel HamburgerPanel => _hamburgerPanel;

        public void Configure(
            WorldMapHud hud,
            MobInfoPanel mobInfoPanel,
            InventoryPanel inventoryPanel,
            CharacterPanel characterPanel,
            PlaceholderPanel skillsPanel,
            PlaceholderPanel settingsPanel,
            PlaceholderPanel hamburgerPanel,
            TextMeshProUGUI toastText,
            GameObject toastRoot)
        {
            _hud = hud;
            _mobInfoPanel = mobInfoPanel;
            _inventoryPanel = inventoryPanel;
            _characterPanel = characterPanel;
            _skillsPanel = skillsPanel;
            _settingsPanel = settingsPanel;
            _hamburgerPanel = hamburgerPanel;
            _toastText = toastText;
            _toastRoot = toastRoot;
        }

        public void NotifyAttacked(string mobName)
        {
            ShowToast("Zostałeś zaatakowany");
            Debug.Log("[WorldMapPrototype] Zostałeś zaatakowany by " + mobName);
        }

        public void RequestArEncounter(string reason)
        {
            Debug.Log("[WorldMapPrototype] AR ENCOUNTER REQUESTED (" + reason + ")");
            ShowToast("AR ENCOUNTER REQUESTED");
        }

        public void ShowToast(string message)
        {
            if (_toastText != null)
            {
                _toastText.text = message;
            }

            if (_toastRoot != null)
            {
                _toastRoot.SetActive(true);
            }

            if (_toastRoutine != null)
            {
                StopCoroutine(_toastRoutine);
            }

            _toastRoutine = StartCoroutine(HideToastAfterDelay());
        }

        public void ShowOnlyPanel(GameObject panel)
        {
            HideAllPanels();
            if (panel == null)
            {
                return;
            }

            panel.SetActive(true);
            _activePanel = panel;
        }

        public void HideAllPanels()
        {
            SetActiveSafe(_mobInfoPanel != null ? _mobInfoPanel.gameObject : null, false);
            SetActiveSafe(_inventoryPanel != null ? _inventoryPanel.gameObject : null, false);
            SetActiveSafe(_characterPanel != null ? _characterPanel.gameObject : null, false);
            SetActiveSafe(_skillsPanel != null ? _skillsPanel.gameObject : null, false);
            SetActiveSafe(_settingsPanel != null ? _settingsPanel.gameObject : null, false);
            SetActiveSafe(_hamburgerPanel != null ? _hamburgerPanel.gameObject : null, false);
            _activePanel = null;
        }

        public void TogglePanel(GameObject panel)
        {
            if (panel == null)
            {
                return;
            }

            if (_activePanel == panel && panel.activeSelf)
            {
                HideAllPanels();
                return;
            }

            ShowOnlyPanel(panel);
        }

        private IEnumerator HideToastAfterDelay()
        {
            yield return new WaitForSecondsRealtime(_toastSeconds);
            if (_toastRoot != null)
            {
                _toastRoot.SetActive(false);
            }

            _toastRoutine = null;
        }

        private static void SetActiveSafe(GameObject go, bool active)
        {
            if (go != null)
            {
                go.SetActive(active);
            }
        }
    }
}
