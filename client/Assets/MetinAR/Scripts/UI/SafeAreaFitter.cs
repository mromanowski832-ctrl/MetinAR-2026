using UnityEngine;

namespace MetinAR.UI
{
    /// <summary>
    /// Applies <see cref="Screen.safeArea"/> to a full-stretch RectTransform.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rect;
        private Rect _lastSafeArea;
        private Vector2Int _lastResolution;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            Apply();
        }

        private void Update()
        {
            if (_lastSafeArea != Screen.safeArea
                || _lastResolution.x != Screen.width
                || _lastResolution.y != Screen.height)
            {
                Apply();
            }
        }

        public void Apply()
        {
            if (_rect == null)
            {
                _rect = (RectTransform)transform;
            }

            Rect safe = Screen.safeArea;
            _lastSafeArea = safe;
            _lastResolution = new Vector2Int(Screen.width, Screen.height);

            Vector2 anchorMin = safe.position;
            Vector2 anchorMax = safe.position + safe.size;
            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;
            _rect.anchorMin = anchorMin;
            _rect.anchorMax = anchorMax;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
