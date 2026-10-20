using System.Collections;
using MetinAR.WorldMap;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// World-map side of the AR encounter transition (prototype): on every encounter request of
    /// <see cref="WorldMapPrototypeController"/> it hands the requested mob data to the AR scene and opens
    /// <see cref="ArEncounterScenes.ArEncounterScenePath"/> after a short delay so the toast stays readable.
    /// </summary>
    public sealed class ArEncounterLauncher : MonoBehaviour
    {
        [SerializeField]
        private WorldMapPrototypeController _controller;

        [SerializeField]
        private string _arScenePath = ArEncounterScenes.ArEncounterScenePath;

        [SerializeField]
        private float _openDelaySeconds = 0.75f;

        [SerializeField]
        private bool _openSceneOnRequest = true;

        private Coroutine _openRoutine;

        /// <summary>When false, requests are handed off but the AR scene is not opened (used by world-map smoke tests).</summary>
        public bool OpenSceneOnRequest
        {
            get => _openSceneOnRequest;
            set => _openSceneOnRequest = value;
        }

        public float OpenDelaySeconds
        {
            get => _openDelaySeconds;
            set => _openDelaySeconds = Mathf.Max(0f, value);
        }

        public string ArScenePath => _arScenePath;

        public ArEncounterRequest LastSubmittedRequest { get; private set; }

        public int SubmittedCount { get; private set; }

        public bool IsOpening => _openRoutine != null;

        public void Configure(WorldMapPrototypeController controller)
        {
            _controller = controller;
        }

        private void Awake()
        {
            if (_controller == null)
            {
                _controller = GetComponent<WorldMapPrototypeController>();
            }

            if (_controller == null)
            {
                _controller = FindAnyObjectByType<WorldMapPrototypeController>();
            }
        }

        private void OnEnable()
        {
            if (_controller != null)
            {
                _controller.EncounterRequested -= OnEncounterRequested;
                _controller.EncounterRequested += OnEncounterRequested;
            }
        }

        private void OnDisable()
        {
            if (_controller != null)
            {
                _controller.EncounterRequested -= OnEncounterRequested;
            }
        }

        private void OnEncounterRequested(string reason)
        {
            PrototypeMob mob = _controller != null ? _controller.LastEncounterMob : null;
            ArEncounterRequest request = mob != null
                ? new ArEncounterRequest(mob.MobName, mob.Level, mob.CurrentHp, mob.MaxHp, mob.Aggressive, reason)
                : ArEncounterRequest.PrototypeTestSpider(reason);
            LastSubmittedRequest = request;
            SubmittedCount++;
            ArEncounterHandoff.Submit(request);
            Debug.Log("[ArEncounterLauncher] request " + request + " open=" + _openSceneOnRequest);

            if (_openSceneOnRequest && _openRoutine == null)
            {
                _openRoutine = StartCoroutine(OpenAfterDelay());
            }
        }

        private IEnumerator OpenAfterDelay()
        {
            if (_openDelaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(_openDelaySeconds);
            }

            Debug.Log("[ArEncounterLauncher] opening " + _arScenePath);
            SceneManager.LoadScene(_arScenePath);
            _openRoutine = null;
        }
    }
}
