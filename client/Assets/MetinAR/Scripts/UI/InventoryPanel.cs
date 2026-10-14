using UnityEngine;
using UnityEngine.UI;

namespace MetinAR.UI
{
    /// <summary>
    /// Simple prototype inventory slot grid.
    /// </summary>
    public sealed class InventoryPanel : MonoBehaviour
    {
        [SerializeField]
        private Transform _slotRoot;

        [SerializeField]
        private Button _closeButton;

        [SerializeField]
        private int _slotCount = 16;

        public void Configure(Transform slotRoot, Button closeButton, int slotCount = 16)
        {
            _slotRoot = slotRoot;
            _closeButton = closeButton;
            _slotCount = slotCount;
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

        public Transform SlotRoot => _slotRoot;
        public int SlotCount => _slotCount;
    }
}
