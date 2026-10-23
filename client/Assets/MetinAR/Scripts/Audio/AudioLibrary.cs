using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace MetinAR.Audio
{
    /// <summary>
    /// METINAR-040 (#85): the clips the AudioService may play, keyed by ledger id (path under Assets/MetinAR/Audio,
    /// same key as LICENSES.md), plus the project mixer. Lives at Resources/Audio/MetinARAudioLibrary.asset;
    /// regenerate with MetinAR ▸ Audio ▸ Rebuild Audio Library (AudioLibraryBuilder) after changing AudioCueTable.
    /// </summary>
    [CreateAssetMenu(menuName = "MetinAR/Audio/Audio Library", fileName = "MetinARAudioLibrary")]
    public sealed class AudioLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id;
            public AudioClip clip;
        }

        [SerializeField]
        private AudioMixer _mixer;

        [SerializeField]
        private List<Entry> _clips = new List<Entry>();

        private Dictionary<string, AudioClip> _index;

        public AudioMixer Mixer => _mixer;
        public IReadOnlyList<Entry> Clips => _clips;

        public bool TryGetClip(string id, out AudioClip clip)
        {
            if (_index == null) BuildIndex();
            return _index.TryGetValue(id ?? string.Empty, out clip) && clip != null;
        }

        public void Set(AudioMixer mixer, List<Entry> clips)
        {
            _mixer = mixer;
            _clips = clips ?? new List<Entry>();
            _index = null;
        }

        private void OnEnable() => _index = null;

        private void BuildIndex()
        {
            _index = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
            foreach (Entry e in _clips)
                if (e != null && !string.IsNullOrEmpty(e.id) && !_index.ContainsKey(e.id))
                    _index.Add(e.id, e.clip);
        }
    }
}
