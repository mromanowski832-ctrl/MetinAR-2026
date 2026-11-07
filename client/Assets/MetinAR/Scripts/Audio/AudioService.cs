using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace MetinAR.Audio
{
    /// <summary>
    /// METINAR-040 (#85): runtime audio. One persistent object (created before the first scene loads) that
    /// listens to <see cref="GameAudio"/>, plays cues from <see cref="AudioCueTable"/> through a pool of AudioSources
    /// routed to the SFX / UI mixer groups, holds the Music source for #68, applies the player volumes to the exposed
    /// mixer parameters and pauses all audio while the app is in the background.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioService : MonoBehaviour, IAudioOutput
    {
        public const string LibraryResourcePath = "Audio/MetinARAudioLibrary";
        public const string LogPrefix = "[Audio] ";
        public const int SfxVoices = 8;
        public const int UiVoices = 3;
        public const float MusicFadeSeconds = 0.6f;

        /// <summary>Set false (e.g. in an editor test) before entering Play Mode to skip the automatic bootstrap.</summary>
        public static bool AutoBootstrap = true;

        private readonly List<Voice> _sfx = new List<Voice>();
        private readonly List<Voice> _ui = new List<Voice>();
        private readonly Dictionary<GameAudioLoop, AudioSource> _loops = new Dictionary<GameAudioLoop, AudioSource>();
        private readonly Dictionary<GameAudioLoop, bool> _loopWanted = new Dictionary<GameAudioLoop, bool>();

        private AudioLibrary _library;
        private AudioMixerGroup _sfxGroup;
        private AudioMixerGroup _uiGroup;
        private AudioMixerGroup _musicGroup;
        private AudioSource _music;
        private Coroutine _musicFade;
        private bool _mixerVolumesApplied;
        private bool _mixerStateLogged;
        private bool _paused;
        private UnityHaptics _haptics;

        public static AudioService Instance { get; private set; }

        public AudioSettingsModel Settings { get; private set; }
        public AudioEventDirector Director { get; private set; }
        public AudioLibrary Library => _library;
        public AudioSource MusicSource => _music;
        public bool MixerVolumesApplied => _mixerVolumesApplied;
        public bool IsPaused => _paused;
        public int HapticCalls => _haptics != null ? _haptics.Calls : 0;

        /// <summary>Log each played cue ("[Audio] play event=… clip=…"); on in development builds and the editor.</summary>
        public bool VerboseLog { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!AutoBootstrap || Instance != null) return;
            Create();
        }

        /// <summary>Creates the service if needed (idempotent).</summary>
        public static AudioService Create()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("MetinARAudio");
            DontDestroyOnLoad(go);
            var service = go.AddComponent<AudioService>();
            go.AddComponent<AudioGameEventBridge>();
            go.AddComponent<AudioSettingsOverlay>();
            return service;
        }

        /// <summary>Raise <paramref name="gameEvent"/> (same as <see cref="GameAudio.Raise"/>).</summary>
        public void Play(GameAudioEvent gameEvent) => Director.Handle(gameEvent);

        public void SetVolume(AudioChannel channel, float value) => Settings.SetVolume(channel, value);

        public void SetHaptics(bool enabled) => Settings.HapticsEnabled = enabled;

        /// <summary>#68 entry point: loop <paramref name="clip"/> on the Music group with a short fade (no click).</summary>
        public void PlayMusic(AudioClip clip)
        {
            if (clip == null) { StopMusic(); return; }
            if (_music.clip == clip && _music.isPlaying) return;
            _music.clip = clip;
            _music.volume = 0f;
            _music.Play();
            FadeMusic(1f, false);
        }

        public void StopMusic()
        {
            if (_music.isPlaying) FadeMusic(0f, true);
        }

        void IAudioOutput.PlayOneShot(string clipId, AudioChannel channel, float volume, float pitch)
        {
            if (_library == null || !_library.TryGetClip(clipId, out AudioClip clip))
            {
                Debug.LogWarning(LogPrefix + "missing clip " + clipId);
                return;
            }

            List<Voice> pool = channel == AudioChannel.Ui ? _ui : _sfx;
            Voice voice = TakeVoice(pool);
            AudioSource src = voice.Source;
            src.clip = clip;
            src.pitch = pitch;
            src.volume = volume * FallbackGain(channel);
            src.Play();
            voice.StartedAt = Time.unscaledTime;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            VerboseLog = Debug.isDebugBuild || Application.isEditor;
            _library = Resources.Load<AudioLibrary>(LibraryResourcePath);
            if (_library == null) Debug.LogError(LogPrefix + "AudioLibrary not found at Resources/" + LibraryResourcePath);
            ResolveGroups();

            for (int i = 0; i < SfxVoices; i++) _sfx.Add(new Voice(NewSource("Sfx_" + i, _sfxGroup)));
            for (int i = 0; i < UiVoices; i++) _ui.Add(new Voice(NewSource("Ui_" + i, _uiGroup)));
            _music = NewSource("Music", _musicGroup);
            _music.loop = true;
            _music.priority = 0;

            Settings = new AudioSettingsModel(new PlayerPrefsAudioSettingsStore());
            _haptics = new UnityHaptics();
            Director = new AudioEventDirector(this, _haptics, Settings, () => Time.unscaledTimeAsDouble);
            Director.Played += OnPlayed;
            Settings.Changed += ApplyVolumes;
            GameAudio.Raised += OnRaised;
            GameAudio.LoopChanged += OnLoopChanged;
        }

        private void Start()
        {
            // AudioMixer.SetFloat is ignored before the mixer is initialised (Awake); apply here and on every change.
            ApplyVolumes();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            GameAudio.Raised -= OnRaised;
            GameAudio.LoopChanged -= OnLoopChanged;
            if (Settings != null) Settings.Changed -= ApplyVolumes;
            Instance = null;
        }

        private void OnApplicationPause(bool paused) => SetPaused(paused);

        private void OnApplicationFocus(bool focused)
        {
            // In the editor, clicking another window must not silence Play Mode.
            if (!Application.isEditor) SetPaused(!focused);
        }

        private void SetPaused(bool paused)
        {
            if (_paused == paused) return;
            _paused = paused;
            // AudioListener.pause pauses every source in place (no restart burst, no pop); resume continues the
            // same samples. Anti-spam history is reset so a resume does not replay stale cues.
            AudioListener.pause = paused;
            if (!paused)
            {
                Director?.ResetHistory();
                ApplyVolumes();
            }

            if (VerboseLog) Debug.Log(LogPrefix + (paused ? "paused" : "resumed"));
        }

        private void OnRaised(GameAudioEvent gameEvent)
        {
            if (_paused || Director == null) return;
            Director.Handle(gameEvent);
        }

        private void OnPlayed(GameAudioEvent gameEvent, string clip)
        {
            if (VerboseLog) Debug.Log(LogPrefix + "play event=" + gameEvent + " clip=" + clip);
        }

        private void OnLoopChanged(GameAudioLoop loop, bool playing)
        {
            _loopWanted[loop] = playing;
            if (!_loops.TryGetValue(loop, out AudioSource src))
            {
                if (!playing) return;
                src = NewSource("Loop_" + loop, _sfxGroup);
                src.loop = true;
                if (_library != null && _library.TryGetClip(AudioCueTable.LoopClip(loop), out AudioClip clip)) src.clip = clip;
                _loops[loop] = src;
            }

            if (src.clip == null) return;
            StartCoroutine(FadeLoop(loop, src, playing));
            if (VerboseLog) Debug.Log(LogPrefix + "loop " + loop + (playing ? " on" : " off"));
        }

        private IEnumerator FadeLoop(GameAudioLoop loop, AudioSource src, bool playing)
        {
            const float seconds = 0.15f;
            float target = playing ? AudioCueTable.LoopVolume(loop) * FallbackGain(AudioChannel.Sfx) : 0f;
            if (playing && !src.isPlaying)
            {
                src.volume = 0f;
                src.Play();
            }

            float start = src.volume;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                if (_loopWanted.TryGetValue(loop, out bool wanted) && wanted != playing) yield break;
                src.volume = Mathf.Lerp(start, target, t / seconds);
                yield return null;
            }

            src.volume = target;
            if (!playing) src.Stop();
        }

        private void FadeMusic(float target, bool stopAtEnd)
        {
            if (_musicFade != null) StopCoroutine(_musicFade);
            _musicFade = StartCoroutine(FadeMusicRoutine(target * FallbackGain(AudioChannel.Music), stopAtEnd));
        }

        private IEnumerator FadeMusicRoutine(float target, bool stopAtEnd)
        {
            float start = _music.volume;
            for (float t = 0f; t < MusicFadeSeconds; t += Time.unscaledDeltaTime)
            {
                _music.volume = Mathf.Lerp(start, target, t / MusicFadeSeconds);
                yield return null;
            }

            _music.volume = target;
            if (stopAtEnd) _music.Stop();
            _musicFade = null;
        }

        private void ApplyVolumes()
        {
            AudioMixer mixer = _library != null ? _library.Mixer : null;
            bool ok = mixer != null;
            if (ok)
            {
                ok &= mixer.SetFloat(AudioSettingsModel.MasterParameter, AudioSettingsModel.ToDecibels(Settings.Master));
                foreach (AudioChannel ch in new[] { AudioChannel.Sfx, AudioChannel.Ui, AudioChannel.Music })
                    ok &= mixer.SetFloat(AudioSettingsModel.MixerParameter(ch), AudioSettingsModel.ToDecibels(Settings.GetVolume(ch)));
            }

            if (!_mixerStateLogged || ok != _mixerVolumesApplied)
                Debug.Log(LogPrefix + (ok ? "mixer volumes applied" : "mixer parameters unavailable - using per-source volume"));
            _mixerVolumesApplied = ok;
            _mixerStateLogged = true;

            if (_music != null && _music.isPlaying && _musicFade == null) _music.volume = FallbackGain(AudioChannel.Music);
            foreach (KeyValuePair<GameAudioLoop, AudioSource> kv in _loops)
                if (kv.Value.isPlaying) kv.Value.volume = AudioCueTable.LoopVolume(kv.Key) * FallbackGain(AudioChannel.Sfx);
        }

        /// <summary>1 when the mixer applies the volumes; otherwise the per-source linear gain.</summary>
        private float FallbackGain(AudioChannel channel) => _mixerVolumesApplied ? 1f : Settings.EffectiveVolume(channel);

        private void ResolveGroups()
        {
            AudioMixer mixer = _library != null ? _library.Mixer : null;
            if (mixer == null) return;
            _sfxGroup = FindGroup(mixer, "SFX");
            _uiGroup = FindGroup(mixer, "UI");
            _musicGroup = FindGroup(mixer, "Music");
        }

        private static AudioMixerGroup FindGroup(AudioMixer mixer, string name)
        {
            foreach (AudioMixerGroup g in mixer.FindMatchingGroups(name))
                if (g.name == name) return g;
            Debug.LogWarning(LogPrefix + "mixer group missing: " + name);
            return null;
        }

        private AudioSource NewSource(string name, AudioMixerGroup group)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.outputAudioMixerGroup = group;
            src.priority = 64;
            return src;
        }

        private static Voice TakeVoice(List<Voice> pool)
        {
            Voice oldest = pool[0];
            foreach (Voice v in pool)
            {
                if (!v.Source.isPlaying) return v;
                if (v.StartedAt < oldest.StartedAt) oldest = v;
            }

            oldest.Source.Stop();
            return oldest;
        }

        private sealed class Voice
        {
            public Voice(AudioSource source) => Source = source;
            public AudioSource Source { get; }
            public float StartedAt { get; set; }
        }
    }
}
