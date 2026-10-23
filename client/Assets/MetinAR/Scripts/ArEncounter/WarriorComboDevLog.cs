using UnityEngine;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// METINAR-039 (#82): Unity sink for the QA <c>[Combo] step=N</c> / <c>[Combo] heavy</c> lines (#100 collector, #90 protocol).
    /// Logs only in the Editor and Development builds (same gate as <c>PlayerLocationService</c> diagnostics:
    /// <c>UNITY_EDITOR || DEVELOPMENT_BUILD</c>); in release builds <see cref="Attach"/> subscribes nothing and returns null.
    /// Call once when the combat view creates its <see cref="WarriorComboMachine"/>; dispose the result with the machine.
    /// </summary>
    public static class WarriorComboDevLog
    {
        public static ComboStepLogger Attach(WarriorComboMachine machine)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (machine == null) return null;
            return new ComboStepLogger(machine, line => Debug.Log(line));
#else
            return null;
#endif
        }
    }
}
