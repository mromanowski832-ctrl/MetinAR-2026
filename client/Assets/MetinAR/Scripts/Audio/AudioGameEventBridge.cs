using System.Collections.Generic;
using MetinAR.ArEncounter;
using MetinAR.Gameplay.Combat;
using MetinAR.Gameplay;
using MetinAR.WorldMap;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MetinAR.Audio
{
    /// <summary>
    /// METINAR-040 (#85): connects the existing gameplay events to <see cref="GameAudio"/> without editing the combat
    /// code (FppCombatController / ArEncounterSceneController / ArEncounterCombat / EncounterCombatSession stay
    /// untouched): it only subscribes to their public events and reads the public FPP timeline. Sound moments that
    /// have no public event yet are listed in docs/METINAR-040-AUDIO-SERVICE.md as one-line GameAudio.Raise hooks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioGameEventBridge : MonoBehaviour
    {
        /// <summary>Buttons whose sound comes from gameplay (swing / Aura), not from a UI click.</summary>
        public const string CombatButtonPrefix = "Action_";
        private const float ButtonScanSeconds = 1f;

        private readonly List<GameAudioEvent> _buffer = new List<GameAudioEvent>(8);
        private readonly FppAudioObserver _fpp = new FppAudioObserver();

        private ArEncounterSceneController _ar;
        private ArEncounterCombat _combat;
        private FppCombatController _fppController;
        private WorldMapPrototypeController _worldMap;
        private bool _settingsPanelWasOpen;
        private float _nextButtonScan;

        public int ButtonsWired { get; private set; }
        public bool HasArEncounter => _ar != null;
        public bool HasWorldMap => _worldMap != null;
        public bool AuraLoopOn => _fpp.LoopPlaying;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Unbind();
        }

        /// <summary>Re-scan the active scene (used by the scene-load callback and the editor smoke test).</summary>
        public void BindScene()
        {
            Unbind();
            _ar = FindFirstObjectByType<ArEncounterSceneController>();
            _worldMap = FindFirstObjectByType<WorldMapPrototypeController>();

            if (_ar != null)
            {
                _ar.MobPlaced += OnMobPlaced;
                GameAudio.Raise(GameAudioEvent.ArEnter);
            }

            if (_worldMap != null) _worldMap.PlayerAttacked += OnPlayerAttacked;
            AudioService.Instance?.Director?.ResetHistory();
            WireButtons();
            _nextButtonScan = Time.unscaledTime + ButtonScanSeconds;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindScene();

        private void OnSceneUnloaded(Scene scene)
        {
            if (_ar != null && _ar.gameObject.scene == scene) Unbind();
        }

        private void Update()
        {
            if (_ar != null)
            {
                if (_combat == null && _ar.Combat != null)
                {
                    _combat = _ar.Combat;
                    _combat.HitResolved += OnHitResolved;
                }

                if (_fppController == null && _ar.FppCombat != null)
                {
                    _fppController = _ar.FppCombat;
                    _fpp.Reset();
                }

                if (_fppController != null) ObserveFpp(_fppController.Timeline);
            }

            if (_worldMap != null)
            {
                bool open = _worldMap.SettingsPanel != null && _worldMap.SettingsPanel.gameObject.activeInHierarchy;
                if (open && !_settingsPanelWasOpen)
                {
                    AudioSettingsOverlay overlay = GetComponent<AudioSettingsOverlay>();
                    if (overlay != null) overlay.Open();
                }

                _settingsPanelWasOpen = open;
            }

            if (Time.unscaledTime >= _nextButtonScan)
            {
                _nextButtonScan = Time.unscaledTime + ButtonScanSeconds;
                WireButtons();
            }
        }

        private void ObserveFpp(FppCombatTimeline timeline)
        {
            _buffer.Clear();
            bool? loop = _fpp.Observe(timeline.SwingId, timeline.Action == FppCombatAction.Aura, timeline.AuraActive, _buffer);
            RaiseBuffer();
            if (loop.HasValue) GameAudio.SetLoop(GameAudioLoop.Aura, loop.Value);
        }

        private void OnHitResolved(CombatHitReport report)
        {
            if (report == null) return;
            _buffer.Clear();
            CombatAudioRules.ForHit(report.Status == CombatHitStatus.Resolved, report.Damage.Landed,
                report.Damage.Outcome == HitOutcome.Blocked || report.Damage.Outcome == HitOutcome.Parried, report.Damage.IsCritical, report.Killed,
                report.LevelsGained, _buffer);
            RaiseBuffer();
        }

        private void OnMobPlaced(ArEncounterMob mob) => GameAudio.Raise(GameAudioEvent.MobAppear);

        private void OnPlayerAttacked(string mobName)
        {
            GameAudio.Raise(GameAudioEvent.MobAttack);
            GameAudio.Raise(GameAudioEvent.PlayerHurt);
        }

        private void RaiseBuffer()
        {
            foreach (GameAudioEvent e in _buffer) GameAudio.Raise(e);
            _buffer.Clear();
        }

        private void WireButtons()
        {
            // Includes inactive panels; scene objects only (skips prefab assets / editor objects).
            Button[] buttons = Resources.FindObjectsOfTypeAll<Button>();
            foreach (Button b in buttons)
            {
                if (b == null || !b.gameObject.scene.IsValid() || b.name.StartsWith(CombatButtonPrefix)) continue;
                if (b.GetComponent<UiClickSound>() != null) continue;
                b.gameObject.AddComponent<UiClickSound>();
                ButtonsWired++;
            }
        }

        private void Unbind()
        {
            if (_ar != null) _ar.MobPlaced -= OnMobPlaced;
            if (_combat != null) _combat.HitResolved -= OnHitResolved;
            if (_worldMap != null) _worldMap.PlayerAttacked -= OnPlayerAttacked;
            if (_fpp.LoopPlaying) GameAudio.SetLoop(GameAudioLoop.Aura, false);
            _fpp.Reset();
            _ar = null;
            _combat = null;
            _fppController = null;
            _worldMap = null;
            _settingsPanelWasOpen = false;
        }
    }
}
