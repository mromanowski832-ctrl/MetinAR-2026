using UnityEngine;
using UnityEngine.EventSystems;

namespace MetinAR.WorldMap
{
    /// <summary>
    /// Forwards pointer clicks (EventSystem + PhysicsRaycaster) to <see cref="PrototypeMob.OpenInfoPanel"/>.
    /// </summary>
    public sealed class MobClickTarget : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField]
        private PrototypeMob _mob;

        public void Configure(PrototypeMob mob)
        {
            _mob = mob;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            _mob?.OpenInfoPanel();
        }
    }
}
