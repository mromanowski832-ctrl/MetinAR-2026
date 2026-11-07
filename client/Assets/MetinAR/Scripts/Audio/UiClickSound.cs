using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MetinAR.Audio
{
    /// <summary>
    /// Click feedback for a uGUI button: UiClick when interactable, UiDenied when disabled. Added at runtime by
    /// <see cref="AudioGameEventBridge"/> to the scene buttons, so the scene builders need no change.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiClickSound : MonoBehaviour, IPointerClickHandler
    {
        private Selectable _selectable;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
            if (_selectable == null) _selectable = GetComponent<Selectable>();
            bool enabled = _selectable == null || _selectable.IsInteractable();
            GameAudio.Raise(enabled ? GameAudioEvent.UiClick : GameAudioEvent.UiDenied);
        }
    }
}
