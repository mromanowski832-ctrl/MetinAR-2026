using MetinAR.WorldMap;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MetinAR.Presentation
{
    /// <summary>Replaces the map NPC's body while retaining its placement, label and entity identity.</summary>
    public sealed class NpcModelPresenter : MonoBehaviour, IPointerClickHandler
    {
        private Animator _animator;
        private GameFrontend _frontend;
        public GameObject Model { get; private set; }

        public static void Attach(PrototypeNpc npc, GameArtCatalog catalog, GameFrontend frontend)
        {
            if (npc == null || catalog == null || catalog.WanderingMerchant == null
                || npc.GetComponent<NpcModelPresenter>() != null) return;
            var presenter = npc.gameObject.AddComponent<NpcModelPresenter>();
            presenter._frontend = frontend;
            presenter.Build(catalog.WanderingMerchant);
        }

        private void Build(GameObject prefab)
        {
            Renderer[] oldRenderers = GetComponentsInChildren<Renderer>(true);
            Collider[] oldColliders = GetComponentsInChildren<Collider>(true);
            Model = Instantiate(prefab, transform, false);
            Model.name = "ProductionWanderingMerchant";
            if (Model.GetComponentInChildren<SkinnedMeshRenderer>(true) == null
                || Model.GetComponentInChildren<Collider>(true) == null)
            {
                Debug.LogError("[MetinAR Art] Merchant prefab has no skinned model/collider; keeping the existing NPC.");
                if (Application.isPlaying) Destroy(Model); else DestroyImmediate(Model);
                Model = null;
                return;
            }
            foreach (Renderer renderer in oldRenderers)
                if (renderer.GetComponent<TMP_Text>() == null) renderer.enabled = false;
            foreach (Collider collider in oldColliders) collider.enabled = false;
            _animator = Model.GetComponentInChildren<Animator>(true);
            Play("Idle");
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || Model == null || _frontend == null) return;
            // A rejected (e.g. queued behind another modal) click must not leave the NPC talking forever.
            if (_frontend.ShowMerchant(this)) Play("Talk");
        }

        public void EndConversation() => Play("Idle");

        private void Play(string state)
        {
            if (_animator == null || !_animator.isActiveAndEnabled || !_animator.isInitialized
                || _animator.runtimeAnimatorController == null) return;
            int id = Animator.StringToHash("Base Layer." + state);
            if (_animator.HasState(0, id)) _animator.CrossFade(id, 0.12f);
        }
    }
}
