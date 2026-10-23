using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MetinAR.Presentation
{
    /// <summary>Small uGUI construction helpers; labels remain TMP text rather than baked-in bitmap copy.</summary>
    internal static class GameMenuUi
    {
        internal static readonly Color Ink = new Color(0.035f, 0.043f, 0.06f, 0.98f);
        internal static readonly Color Panel = new Color(0.075f, 0.085f, 0.11f, 1f);
        internal static readonly Color Gold = new Color(0.84f, 0.68f, 0.38f, 1f);
        internal static readonly Color White = new Color(0.94f, 0.93f, 0.89f, 1f);
        internal static readonly Color Muted = new Color(0.64f, 0.68f, 0.73f, 1f);

        internal static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        internal static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * inset;
            rect.offsetMax = Vector2.one * -inset;
        }

        internal static Image Image(string name, Transform parent, Color color)
        {
            var image = Rect(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        internal static TextMeshProUGUI Text(string name, Transform parent, string value, float size,
            Color color, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.enableWordWrapping = true;
            return text;
        }

        internal static void Height(RectTransform rect, float height)
        {
            var layout = rect.gameObject.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;
        }

        internal static TextMeshProUGUI Paragraph(Transform parent, string value, float size, float height,
            Color? color = null, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var text = Text("Text", parent, value, size, color ?? White, alignment);
            Height(text.rectTransform, height);
            return text;
        }

        internal static Button Button(Transform parent, string label, UnityAction action, bool primary = false)
        {
            var background = Image("Button_" + label, parent, primary ? Gold : Panel);
            background.raycastTarget = true;
            Height(background.rectTransform, 92f);
            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.14f, 1.14f, 1.14f);
            colors.pressedColor = new Color(0.76f, 0.76f, 0.76f);
            button.colors = colors;
            var title = Text("Label", background.transform, label, 31f, primary ? Ink : White,
                TextAlignmentOptions.Center);
            Stretch(title.rectTransform, 14f);
            button.onClick.AddListener(action);
            return button;
        }

        internal static RectTransform Vertical(Transform parent, string name, float spacing = 16f)
        {
            RectTransform rect = Rect(name, parent);
            var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;
            return rect;
        }

        internal static Slider Slider(Transform parent, float value, UnityAction<float> changed)
        {
            RectTransform root = Rect("VolumeSlider", parent);
            Height(root, 76f);
            var slider = root.gameObject.AddComponent<Slider>();
            var track = Image("Track", root, Panel);
            track.raycastTarget = true;
            Stretch(track.rectTransform);
            track.rectTransform.anchorMin = new Vector2(0f, 0.35f);
            track.rectTransform.anchorMax = new Vector2(1f, 0.65f);
            var fillArea = Rect("FillArea", root);
            Stretch(fillArea, 16f);
            var fill = Image("Fill", fillArea, Gold);
            Stretch(fill.rectTransform);
            slider.fillRect = fill.rectTransform;
            var handleArea = Rect("HandleArea", root);
            Stretch(handleArea, 16f);
            var handle = Image("Handle", handleArea, White);
            handle.raycastTarget = true;
            handle.rectTransform.sizeDelta = new Vector2(32f, 64f);
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(changed);
            return slider;
        }
    }
}
