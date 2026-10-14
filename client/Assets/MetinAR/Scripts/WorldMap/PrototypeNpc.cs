using UnityEngine;

namespace MetinAR.WorldMap
{
    /// <summary>
    /// Stationary prototype NPC — visual presence only.
    /// </summary>
    public sealed class PrototypeNpc : MonoBehaviour
    {
        [SerializeField]
        private string _npcName = "Wędrowny Handlarz";

        public string NpcName => _npcName;

        public void Configure(string npcName)
        {
            _npcName = npcName;
        }
    }
}
