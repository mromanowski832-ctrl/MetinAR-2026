using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MetinAR.Audio
{
    /// <summary>
    /// METINAR-040 (#85): minimal sound settings screen built at runtime on its own overlay canvas — separate SFX /
    /// Music / UI sliders and the vibration switch, persisted by <see cref="AudioSettingsModel"/> (PlayerPrefs until
    /// #71). Opened by the "Dźwięk" button (top-right, under the radar) or by the world-map "Ustawienia" panel.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioSettingsOverlay : MonoBehaviour
    {
        public const int SortingOrder = 900;
        public const string OpenButtonName = "AudioSettingsButton";
        public const string PanelName = "AudioSettingsPanel";

        private static readonly Color Leather = new Color(0.23f, 0.15f, 0.09f, 0.96f);
        private static readonly Color Parchment = new Color(0.93f, 0.86f, 0.70f, 1f);
        private static readonly Color Gold = new Color(0.85f, 0.67f, 0.30f, 1f);
        private static readonly Color Dark = new Color(0.08f, 0.06f, 0.04f, 0.9f);

        private GameObject _canvas;
        private GameObject _panel;
        private Slider _sfx;
        private Slider _music;
        private Slider _ui;
        private TextMeshProUGUI _hapticsLabel;
        private bool _syncing;

        public bool IsOpen => _panel != null && _panel.activeSelf;
        public Slider SfxSlider => _sfx;
        public Slider MusicSlider => _music;
        public Slider UiSlider => _ui;

        public void Open()
        {
            EnsureBuilt();
            if (_panel == null) return;
            Sync();
            _panel.SetActive(true);
        }

        public void Close()
        {
            if (_panel != null) _panel.SetActive(false);
        }

        public void ToggleHaptics()
        {
            AudioSettingsModel s = Settings;
            if (s == null) return;
            s.HapticsEnabled = !s.HapticsEnabled;
            Sync();
        }

        private static AudioSettingsModel Settings => AudioService.Instance != null ? AudioService.Instance.Settings : null;

        private void Start() => EnsureBuilt();

        private void EnsureBuilt()
        {
            if (_canvas != null || Settings == null) return;

            _canvas = new GameObject("AudioSettingsCanvas", typeof(RectTransform));
            _canvas.transform.SetParent(transform, false);
            var canvas = _canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = _canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvas.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)_canvas.transform;

            Button open = MakeButton(OpenButtonName, root, "Dźwięk", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-20f, -250f), new Vector2(150f, 72f), 28);
            open.onClick.AddListener(() => { if (IsOpen) Close(); else Open(); });

            _panel = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
            var panelRt = (RectTransform)_panel.transform;
            panelRt.SetParent(root, false);
            Place(panelRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 820f));
            _panel.GetComponent<Image>().color = Leather;

            Label(panelRt, "Title", "Dźwięk", new Vector2(0f, -40f), new Vector2(760f, 80f), 48, Gold);
            _sfx = Row(panelRt, "Sfx", "Efekty", -170f, AudioChannel.Sfx);
            _music = Row(panelRt, "Music", "Muzyka", -310f, AudioChannel.Music);
            _ui = Row(panelRt, "Ui", "Interfejs", -450f, AudioChannel.Ui);

            Button haptics = MakeButton("HapticsToggle", panelRt, "Wibracje", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -560f), new Vector2(520f, 90f), 32);
            _hapticsLabel = haptics.GetComponentInChildren<TextMeshProUGUI>();
            haptics.onClick.AddListener(ToggleHaptics);

            Button close = MakeButton("Close", panelRt, "Zamknij", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 50f), new Vector2(360f, 90f), 32);
            close.onClick.AddListener(Close);

            _panel.SetActive(false);
            Sync();
        }

        private void Sync()
        {
            AudioSettingsModel s = Settings;
            if (s == null || _sfx == null) return;
            _syncing = true;
            _sfx.value = s.GetVolume(AudioChannel.Sfx);
            _music.value = s.GetVolume(AudioChannel.Music);
            _ui.value = s.GetVolume(AudioChannel.Ui);
            _syncing = false;
            if (_hapticsLabel != null) _hapticsLabel.text = s.HapticsEnabled ? "Wibracje: WŁ" : "Wibracje: WYŁ";
        }

        private Slider Row(RectTransform parent, string id, string title, float y, AudioChannel channel)
        {
            Label(parent, id + "Label", title, new Vector2(-250f, y), new Vector2(240f, 70f), 32, Parchment, TextAlignmentOptions.Left);
            TextMeshProUGUI pct = Label(parent, id + "Value", "100%", new Vector2(330f, y), new Vector2(130f, 70f), 30, Parchment, TextAlignmentOptions.Right);

            var go = new GameObject(id + "Slider", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Place(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(70f, y), new Vector2(380f, 56f));

            Image bg = Child("Background", rt, Dark);
            Stretch(bg.rectTransform, 0f);
            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            var fillAreaRt = (RectTransform)fillArea.transform;
            fillAreaRt.SetParent(rt, false);
            Stretch(fillAreaRt, 6f);
            Image fill = Child("Fill", fillAreaRt, Gold);
            Stretch(fill.rectTransform, 0f);
            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            var handleAreaRt = (RectTransform)handleArea.transform;
            handleAreaRt.SetParent(rt, false);
            Stretch(handleAreaRt, 0f);
            handleAreaRt.offsetMin = new Vector2(20f, 0f);
            handleAreaRt.offsetMax = new Vector2(-20f, 0f);
            Image handle = Child("Handle", handleAreaRt, Parchment);
            handle.rectTransform.sizeDelta = new Vector2(44f, 0f);
            handle.rectTransform.anchorMin = new Vector2(0f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0f, 1f);

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.onValueChanged.AddListener(v =>
            {
                pct.text = Mathf.RoundToInt(v * 100f) + "%";
                if (!_syncing && Settings != null) Settings.SetVolume(channel, v);
            });
            return slider;
        }

        private static Button MakeButton(string name, RectTransform parent, string text, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pos, Vector2 size, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Place(rt, anchorMin, anchorMax, pos, size);
            rt.pivot = anchorMin;
            go.GetComponent<Image>().color = Leather;
            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = Gold;
            outline.effectDistance = new Vector2(2f, -2f);
            TextMeshProUGUI label = Label(rt, "Label", text, Vector2.zero, size, fontSize, Parchment);
            Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            return go.GetComponent<Button>();
        }

        private static TextMeshProUGUI Label(RectTransform parent, string name, string text, Vector2 pos, Vector2 size,
            float fontSize, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Place(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), pos, size);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Image Child(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
