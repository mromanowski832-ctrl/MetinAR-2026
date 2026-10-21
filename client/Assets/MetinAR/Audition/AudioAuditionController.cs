using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

namespace MetinAR.Audition
{
    /// <summary>
    /// Listening test for the A31 (METINAR-018 #43 / METINAR-033 #62). NOT a game feature and not part of the game build:
    /// it is only built into the separate APK com.metinar.audiotest by MetinAR.Editor.Audition.AudioAuditionBuild.
    /// IMGUI (no EventSystem / Input System dependency), one button per SFX, Aura start -> loop -> stop sequence,
    /// group volume sliders and the name/length/level of the clip being played.
    ///
    /// Group sliders: MetinARMixer has no exposed parameters yet (stage 5), so the SFX / UI sliders scale the volume of
    /// the AudioSources routed to that mixer group; Master scales AudioListener.volume. Routing goes through the mixer.
    /// </summary>
    public sealed class AudioAuditionController : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            public string label;
            public string group;      // "SFX" or "UI" (mixer group)
            public float peakDbfs;    // from the WAV source, for the on-screen info
            public AudioClip clip;
        }

        public AudioMixer mixer;
        public AudioMixerGroup sfxGroup;
        public AudioMixerGroup uiGroup;
        public AudioSource sfxSource;
        public AudioSource uiSource;
        public AudioSource loopSource;
        public AudioClip auraStart;
        public AudioClip auraLoop;
        public AudioClip auraStop;
        public Entry[] entries = new Entry[0];
        public string sourceSha = "";

        private const float AuraBurstSeconds = 0.65f;   // FppCombatTimeline (PR #32): burst at 0.65 s
        private float _master = 1f, _sfx = 1f, _ui = 1f;
        private string _now = "-";
        private float _loopStarted = -1f;
        private Coroutine _sequence;
        private Vector2 _scroll;
        private float _scrollTop = float.MaxValue;

        private void Awake()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.targetFrameRate = 30;
            if (sfxSource != null) sfxSource.outputAudioMixerGroup = sfxGroup;
            if (uiSource != null) uiSource.outputAudioMixerGroup = uiGroup;
            if (loopSource != null)
            {
                loopSource.outputAudioMixerGroup = sfxGroup;
                loopSource.loop = true;
                loopSource.clip = auraLoop;
            }
        }

        private void ApplyVolumes()
        {
            AudioListener.volume = _master;
            if (sfxSource != null) sfxSource.volume = _sfx;
            if (uiSource != null) uiSource.volume = _ui;
            if (loopSource != null) loopSource.volume = _sfx;
        }

        private void Play(Entry e)
        {
            if (e == null || e.clip == null) return;
            AudioSource s = e.group == "UI" ? uiSource : sfxSource;
            s.PlayOneShot(e.clip);
            _now = string.Format("{0}  ({1:0.00} s, peak {2:0} dBFS, grupa {3})", e.label, e.clip.length, e.peakDbfs, e.group);
        }

        private void StopAll()
        {
            if (_sequence != null) StopCoroutine(_sequence);
            _sequence = null;
            sfxSource.Stop();
            uiSource.Stop();
            loopSource.Stop();
            _loopStarted = -1f;
            _now = "stop";
        }

        private IEnumerator AuraSequence(float loopSeconds, bool withStartAndStop)
        {
            loopSource.Stop();
            double dsp = AudioSettings.dspTime + 0.1;
            if (withStartAndStop)
            {
                sfxSource.clip = auraStart;
                sfxSource.PlayScheduled(dsp);
                loopSource.PlayScheduled(dsp + AuraBurstSeconds);   // loop enters at the burst, like the game hook
                _now = "Aura: start -> petla (od 0,65 s)";
                yield return new WaitForSecondsRealtime(0.1f + AuraBurstSeconds);
            }
            else
            {
                loopSource.PlayScheduled(dsp);
                _now = "Aura: sama petla (szew co 2,0 s)";
                yield return new WaitForSecondsRealtime(0.1f);
            }

            _loopStarted = Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(loopSeconds);
            if (withStartAndStop)
            {
                double stopAt = AudioSettings.dspTime + 0.05;
                sfxSource.clip = auraStop;
                sfxSource.PlayScheduled(stopAt);
                loopSource.SetScheduledEndTime(stopAt + 0.05);
                _now = "Aura: stop";
            }
            else
            {
                loopSource.Stop();
                _now = "Aura: petla zatrzymana";
            }

            _loopStarted = -1f;
            _sequence = null;
        }

        private IEnumerator PoisonTicks()
        {
            Entry tick = Array.Find(entries, e => e.label.Contains("PoisonTick"));
            for (int i = 0; i < 4; i++)
            {
                Play(tick);
                _now = "Trucizna: tik " + (i + 1) + "/4 (co 1 s)";
                yield return new WaitForSecondsRealtime(1f);
            }

            _sequence = null;
        }

        private void StartSequence(IEnumerator routine)
        {
            StopAll();
            _sequence = StartCoroutine(routine);
        }

        private void OnGUI()
        {
            float scale = Mathf.Max(1f, Screen.width / 540f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            float h = Screen.height / scale;
            GUI.skin.button.fontSize = 15;
            GUI.skin.label.fontSize = 15;
            GUI.skin.button.wordWrap = true;

            float top = Screen.safeArea.yMin / scale + 6f;
            GUILayout.BeginArea(new Rect(8, top, w - 16, h - top - 8));
            GUILayout.Label("MetinAR AudioTest  |  zrodlo " + sourceSha + "  |  " + entries.Length + " SFX");
            GUILayout.Label("Teraz: " + _now + (_loopStarted > 0 ? string.Format("   petla {0:0.0} s", Time.realtimeSinceStartup - _loopStarted) : ""));
            Slider("Master", ref _master);
            Slider("SFX", ref _sfx);
            Slider("UI", ref _ui);
            GUILayout.Label("Music: brak klipow (grupa istnieje w mikserze)");
            ApplyVolumes();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Aura jak w grze\nstart+petla 6 s+stop", GUILayout.Height(56))) StartSequence(AuraSequence(6f, true));
            if (GUILayout.Button("Szew petli\nsama petla 30 s", GUILayout.Height(56))) StartSequence(AuraSequence(30f, false));
            if (GUILayout.Button("STOP", GUILayout.Height(56), GUILayout.Width(80))) StopAll();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Trucizna: 4 tiki co 1 s", GUILayout.Height(44))) StartSequence(PoisonTicks());

            // touch-drag scrolling for the clip list only (sliders above keep their own drags)
            Rect marker = GUILayoutUtility.GetRect(0f, 0f);
            if (Event.current.type == EventType.Repaint) _scrollTop = marker.y;
            if (Event.current.type == EventType.MouseDrag && Event.current.mousePosition.y > _scrollTop)
            {
                _scroll.y -= Event.current.delta.y;
                Event.current.Use();
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < entries.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Math.Min(i + 2, entries.Length); j++)
                {
                    if (GUILayout.Button(entries[j].label, GUILayout.Height(52), GUILayout.Width((w - 40) / 2f))) Play(entries[j]);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void Slider(string name, ref float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("{0} {1:0.00} ({2:0.0} dB)", name, value, value > 0.0001f ? 20f * Mathf.Log10(value) : -80f), GUILayout.Width(200));
            value = GUILayout.HorizontalSlider(value, 0f, 1f, GUILayout.Height(30));
            GUILayout.EndHorizontal();
        }
    }
}
