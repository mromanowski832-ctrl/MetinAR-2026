using TMPro;
using UnityEngine;

namespace MetinAR.HandTracking
{
    /// <summary>
    /// Prototype HUD readout for hand tracking: a one-line status ("Ręka: wykryta …") and the 21 detected landmarks as
    /// small dots over the camera image, so the owner can see on the phone that the real hand is being tracked and
    /// that the dots sit on it. Debug aid only; no gameplay function.
    /// </summary>
    public sealed class ArHandDebugOverlay : MonoBehaviour
    {
        public const string NoHandText = "Ręka: pokaż dłoń przed kamerą";
        public const string UnavailableText = "Śledzenie ręki niedostępne";

        [SerializeField]
        private ArHandTracker _tracker;

        [SerializeField]
        private RectTransform _dotsRoot;

        [SerializeField]
        private RectTransform[] _dots;

        [SerializeField]
        private TextMeshProUGUI _label;

        [SerializeField]
        private GameObject _panel;

        public string StatusText => _label != null ? _label.text : null;

        public bool IsVisible => _panel != null && _panel.activeSelf;

        public int VisibleDotCount
        {
            get
            {
                int n = 0;
                if (_dots != null)
                {
                    foreach (RectTransform d in _dots)
                    {
                        if (d != null && d.gameObject.activeSelf)
                        {
                            n++;
                        }
                    }
                }

                return n;
            }
        }

        public void Configure(ArHandTracker tracker, RectTransform dotsRoot, RectTransform[] dots, TextMeshProUGUI label, GameObject panel)
        {
            _tracker = tracker;
            _dotsRoot = dotsRoot;
            _dots = dots;
            _label = label;
            _panel = panel;
        }

        private void LateUpdate()
        {
            if (_tracker == null)
            {
                return;
            }

            bool active = _tracker.IsTrackingActive;
            if (_panel != null && _panel.activeSelf != active)
            {
                _panel.SetActive(active);
            }

            HandObservation o = _tracker.Latest;
            bool hand = active && o != null && o.HasHand && o.Sequence > 0 && Time.time - o.Time < 0.5f;
            if (_label != null)
            {
                string text;
                if (!_tracker.ModelsLoaded && _tracker.ObservationOverride == null)
                {
                    text = string.IsNullOrEmpty(_tracker.LastError) ? UnavailableText : "Ręka: błąd uruchomienia AI (sprawdź log)";
                }
                else if (!string.IsNullOrEmpty(_tracker.LastError))
                {
                    text = "Ręka: błąd analizy obrazu (sprawdź log)";
                }
                else if (hand)
                {
                    text = "Ręka: wykryta " + Mathf.RoundToInt(o.Presence * 100f) + "%"
                           + (o.Source == "camera" ? " · " + Mathf.RoundToInt(o.InferenceMilliseconds) + " ms" : " · test");
                }
                else
                {
                    text = NoHandText;
                }

                if (_label.text != text)
                {
                    _label.text = text;
                }
            }

            if (_dots == null || _dotsRoot == null)
            {
                return;
            }

            Rect rect = _dotsRoot.rect;
            for (int i = 0; i < _dots.Length; i++)
            {
                RectTransform dot = _dots[i];
                if (dot == null)
                {
                    continue;
                }

                bool show = hand && i < HandLandmark.Count;
                if (dot.gameObject.activeSelf != show)
                {
                    dot.gameObject.SetActive(show);
                }

                if (show)
                {
                    System.Numerics.Vector2 v = o.Viewport[i];
                    dot.anchoredPosition = new Vector2(v.X * rect.width, v.Y * rect.height);
                }
            }
        }
    }
}
