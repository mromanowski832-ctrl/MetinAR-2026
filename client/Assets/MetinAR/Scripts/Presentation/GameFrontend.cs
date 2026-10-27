using System.Collections.Generic;
using MetinAR.Progression;
using MetinAR.UI;
using MetinAR.WorldMap;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MetinAR.Presentation
{
    /// <summary>
    /// A map-only frontend. Existing scene names, scene indices, AR handoff and save contracts are retained.
    /// It opens once per app session; the map's menu/settings buttons can reopen it after entering the world.
    /// </summary>
    public sealed class GameFrontend : MonoBehaviour
    {
        private const string VolumeKey = "MetinAR.Presentation.MasterVolume";
        private const string MotionKey = "MetinAR.Presentation.ReducedMotion";
        private static bool _enteredWorld;
        private GameArtCatalog _catalog;
        private GameObject _canvasRoot;
        private RectTransform _body;
        private ScrollRect _scroll;
        private TextMeshProUGUI _heading;
        private TextMeshProUGUI _volumeLabel;
        private Image _emblem;
        private Button _back;
        private NpcModelPresenter _speaker;
        private readonly List<Behaviour> _paused = new List<Behaviour>();
        private readonly List<ModalCanvas> _blockedCanvases = new List<ModalCanvas>();
        private bool _reducedMotion;
        private string _page;

        public bool IsOpen => _canvasRoot != null && _canvasRoot.activeSelf;
        public string CurrentPage => _page;

        private sealed class ModalCanvas
        {
            public CanvasGroup Group;
            public bool Interactable, BlocksRaycasts;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            _enteredWorld = false;
            SceneManager.sceneLoaded -= SceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Subscribe()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneLoaded += SceneLoaded;
        }

        private static void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "WorldMapPrototype" && scene.name != "WorldMapOsmPoc") return;
            if (Object.FindFirstObjectByType<GameFrontend>() != null) return;
            var catalog = GameArtCatalog.Load();
            if (catalog == null)
            {
                Debug.LogError("[MetinAR Art] MetinARPresentation catalog is missing. Run PresentationIntegrationCheck.");
                return;
            }
            var root = new GameObject("MetinARPresentation");
            SceneManager.MoveGameObjectToScene(root, scene);
            var frontend = root.AddComponent<GameFrontend>();
            frontend.Initialize(catalog);
        }

        public void Initialize(GameArtCatalog catalog)
        {
            if (_canvasRoot != null || catalog == null) return;
            _catalog = catalog;
            float volume = PlayerPrefs.GetFloat(VolumeKey, 1f);
            AudioListener.volume = float.IsNaN(volume) || float.IsInfinity(volume) ? 1f : Mathf.Clamp01(volume);
            _reducedMotion = PlayerPrefs.GetInt(MotionKey, 0) != 0;
            BuildFrame();
            foreach (PrototypeNpc npc in Object.FindObjectsByType<PrototypeNpc>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (npc.gameObject.scene == gameObject.scene && npc.NpcName == "Wędrowny Handlarz")
                    NpcModelPresenter.Attach(npc, catalog, this);
            foreach (HudButtonBar bar in Object.FindObjectsByType<HudButtonBar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (bar.gameObject.scene != gameObject.scene) continue;
                if (bar.HamburgerButton != null)
                {
                    bar.HamburgerButton.onClick.RemoveAllListeners();
                    bar.HamburgerButton.onClick.AddListener(ShowHome);
                }
                if (bar.SettingsButton != null)
                {
                    bar.SettingsButton.onClick.RemoveAllListeners();
                    bar.SettingsButton.onClick.AddListener(ShowSettings);
                }
            }
            _canvasRoot.SetActive(false);
            if (!_enteredWorld) ShowHome();
        }

        private void BuildFrame()
        {
            _canvasRoot = GameMenuUi.Rect("MenuCanvas", transform).gameObject;
            var canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasRoot.AddComponent<GraphicRaycaster>();
            Image background = GameMenuUi.Image("Background", _canvasRoot.transform, GameMenuUi.Ink);
            background.raycastTarget = true;
            GameMenuUi.Stretch(background.rectTransform);
            var safe = GameMenuUi.Rect("SafeArea", _canvasRoot.transform);
            GameMenuUi.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var window = GameMenuUi.Rect("Window", safe);
            GameMenuUi.Stretch(window);
            window.anchorMin = new Vector2(0.07f, 0.035f);
            window.anchorMax = new Vector2(0.93f, 0.965f);
            _heading = GameMenuUi.Text("Heading", window, "METINAR", 60f, GameMenuUi.Gold);
            _heading.fontStyle = FontStyles.Bold;
            _heading.characterSpacing = 7f;
            _heading.rectTransform.anchorMin = new Vector2(0f, 1f);
            _heading.rectTransform.anchorMax = Vector2.one;
            _heading.rectTransform.pivot = new Vector2(0.5f, 1f);
            _heading.rectTransform.sizeDelta = new Vector2(0f, 120f);
            _heading.rectTransform.anchoredPosition = Vector2.zero;
            var line = GameMenuUi.Image("GoldRule", window, GameMenuUi.Gold);
            line.rectTransform.anchorMin = new Vector2(0f, 1f);
            line.rectTransform.anchorMax = Vector2.one;
            line.rectTransform.pivot = new Vector2(0.5f, 1f);
            line.rectTransform.sizeDelta = new Vector2(0f, 2f);
            line.rectTransform.anchoredPosition = new Vector2(0f, -124f);

            var scrollRoot = GameMenuUi.Rect("Scroll", window);
            GameMenuUi.Stretch(scrollRoot);
            scrollRoot.offsetMin = new Vector2(0f, 124f);
            scrollRoot.offsetMax = new Vector2(0f, -152f);
            var viewport = GameMenuUi.Rect("Viewport", scrollRoot);
            GameMenuUi.Stretch(viewport);
            // Text and spacing must receive drags through this viewport, not the sibling modal background.
            Image scrollSurface = viewport.gameObject.AddComponent<Image>();
            scrollSurface.color = Color.clear;
            scrollSurface.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            _body = GameMenuUi.Vertical(viewport, "Content", 18f);
            _body.anchorMin = new Vector2(0f, 1f);
            _body.anchorMax = Vector2.one;
            _body.pivot = new Vector2(0.5f, 1f);
            _body.offsetMin = Vector2.zero;
            _body.offsetMax = Vector2.zero;
            _body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = _body;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 36f;
            _back = GameMenuUi.Button(window, "Wróć", Back);
            var backRect = (RectTransform)_back.transform;
            backRect.anchorMin = Vector2.zero;
            backRect.anchorMax = Vector2.right;
            backRect.pivot = new Vector2(0.5f, 0f);
            backRect.anchoredPosition = Vector2.zero;
            backRect.sizeDelta = new Vector2(0f, 96f);
        }

        private void Open(string page, string heading)
        {
            if (!IsOpen)
            {
                // Pause only behaviours that this menu itself found enabled. Do not resume somebody
                // else's disabled mob, modify Time.timeScale, or pause AR/location lifecycle services.
                foreach (PrototypeMob mob in Object.FindObjectsByType<PrototypeMob>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    Pause(mob);
                foreach (EditorMockMovement movement in Object.FindObjectsByType<EditorMockMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    Pause(movement);
                foreach (WorldMapPrototypeController controller in Object.FindObjectsByType<WorldMapPrototypeController>(FindObjectsSortMode.None))
                    if (controller.gameObject.scene == gameObject.scene) controller.HideAllPanels();
                foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (canvas.gameObject.scene != gameObject.scene || canvas.transform.IsChildOf(transform)) continue;
                    CanvasGroup group = canvas.GetComponent<CanvasGroup>();
                    bool added = group == null;
                    if (added) group = canvas.gameObject.AddComponent<CanvasGroup>();
                    _blockedCanvases.Add(new ModalCanvas { Group = group,
                        Interactable = group.interactable, BlocksRaycasts = group.blocksRaycasts });
                    group.interactable = false;
                    group.blocksRaycasts = false;
                }
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            }
            _canvasRoot.SetActive(true);
            _scroll.StopMovement();
            _page = page;
            _heading.text = heading;
            _emblem = null;
            for (int i = _body.childCount - 1; i >= 0; i--)
            {
                GameObject old = _body.GetChild(i).gameObject;
                old.SetActive(false);
                if (Application.isPlaying) Destroy(old); else DestroyImmediate(old);
            }
            _body.anchoredPosition = Vector2.zero;
            _back.gameObject.SetActive(_enteredWorld || page != "home");
        }

        private void Pause(Behaviour behaviour)
        {
            if (behaviour.gameObject.scene != gameObject.scene || !behaviour.enabled) return;
            _paused.Add(behaviour);
            behaviour.enabled = false;
        }

        public void ShowHome()
        {
            EndConversation();
            Open("home", "METINAR");
            GameMenuUi.Paragraph(_body, "PRZYGODA GPS  /  WALKA AR", 25f, 56f, GameMenuUi.Muted);
            var hero = GameMenuUi.Rect("SwordEmblem", _body);
            GameMenuUi.Height(hero, 320f);
            _emblem = GameMenuUi.Image("Sword", hero, Color.white);
            _emblem.sprite = _catalog.Find("starter_sword")?.Icon;
            _emblem.preserveAspect = true;
            GameMenuUi.Stretch(_emblem.rectTransform);
            var progress = LocalPlayerSession.Progress;
            string className = progress.Class.ToString();
            if (className == "Warrior") className = "Wojownik";
            else if (className == "Shaman") className = "Szaman";
            GameMenuUi.Paragraph(_body, className + "  ·  Poziom " + progress.Level, 42f, 80f, GameMenuUi.White);
            GameMenuUi.Paragraph(_body, "EXP " + progress.ExpIntoLevel + " / " + progress.ExpToNextLevel
                + "     •     " + progress.Talary + " talarów", 28f, 64f, GameMenuUi.Muted);
            GameMenuUi.Button(_body, _enteredWorld ? "Wróć do świata" : "Rozpocznij przygodę", EnterWorld, true);
            GameMenuUi.Button(_body, "Katalog przedmiotów", ShowItems);
            GameMenuUi.Button(_body, "Ustawienia", ShowSettings);
            GameMenuUi.Button(_body, "Twórcy i zasoby", ShowCredits);
            GameMenuUi.Paragraph(_body, "Postęp postaci jest obecnie zachowywany w bieżącej sesji gry.",
                23f, 86f, GameMenuUi.Muted);
        }

        public void EnterWorld()
        {
            _enteredWorld = true;
            CloseMenu();
        }

        public void ShowSettings()
        {
            EndConversation();
            Open("settings", "USTAWIENIA");
            _volumeLabel = GameMenuUi.Paragraph(_body, "Głośność: " + Mathf.RoundToInt(AudioListener.volume * 100f) + "%", 34f, 76f);
            GameMenuUi.Slider(_body, AudioListener.volume, value =>
            {
                AudioListener.volume = value;
                PlayerPrefs.SetFloat(VolumeKey, value);
                _volumeLabel.text = "Głośność: " + Mathf.RoundToInt(value * 100f) + "%";
            });
            GameMenuUi.Paragraph(_body, "Ruch interfejsu", 34f, 76f);
            GameMenuUi.Button(_body, _reducedMotion ? "Animacje menu: ograniczone" : "Animacje menu: włączone", () =>
            {
                _reducedMotion = !_reducedMotion;
                PlayerPrefs.SetInt(MotionKey, _reducedMotion ? 1 : 0);
                ShowSettings();
            });
            GameMenuUi.Paragraph(_body, "Ograniczenie ruchu wyłącza animację symbolu w menu. Ustawienia są zapamiętywane na tym urządzeniu.",
                27f, 140f, GameMenuUi.Muted);
        }

        public void ShowItems()
        {
            EndConversation();
            Open("items", "PRZEDMIOTY");
            GameMenuUi.Paragraph(_body, "Katalog wyposażenia, mikstur i materiałów. To podgląd przedmiotów, nie zawartość plecaka.",
                27f, 128f, GameMenuUi.Muted);
            foreach (ItemArt item in _catalog.Items)
            {
                if (item == null) continue;
                ItemArt selected = item;
                var row = GameMenuUi.Button(_body, "", () => ShowItem(selected));
                GameMenuUi.Height((RectTransform)row.transform, 124f);
                var icon = GameMenuUi.Image("Icon", row.transform, Color.white);
                icon.sprite = item.Icon;
                icon.preserveAspect = true;
                icon.rectTransform.anchorMin = Vector2.zero;
                icon.rectTransform.anchorMax = Vector2.up;
                icon.rectTransform.pivot = new Vector2(0f, 0.5f);
                icon.rectTransform.sizeDelta = new Vector2(100f, -16f);
                icon.rectTransform.anchoredPosition = new Vector2(16f, 0f);
                var name = GameMenuUi.Text("Name", row.transform, item.DisplayName, 29f, GameMenuUi.White);
                GameMenuUi.Stretch(name.rectTransform);
                name.rectTransform.offsetMin = new Vector2(136f, 12f);
                name.rectTransform.offsetMax = new Vector2(-24f, -12f);
            }
        }

        private void ShowItem(ItemArt item)
        {
            Open("item", "PRZEDMIOT");
            var icon = GameMenuUi.Image("Icon", _body, Color.white);
            icon.sprite = item.Icon;
            icon.preserveAspect = true;
            GameMenuUi.Height(icon.rectTransform, 360f);
            GameMenuUi.Paragraph(_body, item.DisplayName, 42f, 110f, GameMenuUi.Gold);
            GameMenuUi.Paragraph(_body, item.Description, 30f, 160f);
            GameMenuUi.Button(_body, "Wróć do katalogu", ShowItems);
        }

        public bool ShowMerchant(NpcModelPresenter speaker)
        {
            if (!_enteredWorld || speaker == null || IsOpen) return false;
            _speaker = speaker;
            Open("merchant", "WĘDROWNY HANDLARZ");
            GameMenuUi.Paragraph(_body, "Dobrze cię widzieć, wędrowcze.", 38f, 110f, GameMenuUi.Gold);
            GameMenuUi.Paragraph(_body, "Przejrzyj wyposażenie, mikstury i materiały przydatne w drodze.", 31f, 140f);
            GameMenuUi.Button(_body, "Obejrzyj przedmioty", ShowItems);
            GameMenuUi.Paragraph(_body, "Kupno i sprzedaż nie są jeszcze dostępne.", 26f, 96f, GameMenuUi.Muted);
            GameMenuUi.Button(_body, "Zakończ rozmowę", CloseMenu);
            return true;
        }

        private void ShowCredits()
        {
            Open("credits", "TWÓRCY I ZASOBY");
            GameMenuUi.Paragraph(_body, "MetinAR\nMichał Romanowski", 40f, 160f, GameMenuUi.Gold);
            GameMenuUi.Paragraph(_body, "Własne modele, tekstury i ikony projektu: dłonie i miecz, Wędrowny Handlarz, Lipowy Puklerz oraz 21 ikon przedmiotów.",
                30f, 200f);
            GameMenuUi.Paragraph(_body, "Grafika opisana na tym ekranie jest autorskim zestawem przygotowanym dla MetinAR.",
                26f, 120f, GameMenuUi.Muted);
            GameMenuUi.Paragraph(_body, "Wersja " + Application.version, 26f, 70f, GameMenuUi.Muted);
        }

        public void CloseMenu()
        {
            EndConversation();
            PlayerPrefs.Save();
            _canvasRoot.SetActive(false);
            ResumeWorld();
        }

        private void Back()
        {
            if (_page == "home" && _enteredWorld) CloseMenu();
            else if (_page == "merchant") CloseMenu();
            else if (_page == "item") ShowItems();
            else ShowHome();
        }

        private void EndConversation()
        {
            if (_speaker != null) _speaker.EndConversation();
            _speaker = null;
        }

        private void ResumeWorld()
        {
            foreach (Behaviour behaviour in _paused) if (behaviour != null) behaviour.enabled = true;
            _paused.Clear();
            foreach (ModalCanvas canvas in _blockedCanvases)
            {
                if (canvas.Group == null) continue;
                canvas.Group.interactable = canvas.Interactable;
                canvas.Group.blocksRaycasts = canvas.BlocksRaycasts;
            }
            _blockedCanvases.Clear();
        }

        private void Update()
        {
            if (_emblem != null && IsOpen)
                _emblem.rectTransform.localScale = Vector3.one * (_reducedMotion ? 1f : 1f + Mathf.Sin(Time.unscaledTime * 1.4f) * 0.012f);
        }

        private void OnApplicationPause(bool paused) { if (paused) PlayerPrefs.Save(); }
        private void OnDestroy() { EndConversation(); ResumeWorld(); }
    }
}
