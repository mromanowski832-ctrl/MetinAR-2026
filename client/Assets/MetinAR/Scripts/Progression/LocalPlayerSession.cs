using System;
using MetinAR.Gameplay;
using MetinAR.Gameplay.Combat;
using UnityEngine;

namespace MetinAR.Progression
{
    /// <summary>
    /// Local (client-only) character progress for the current app session (METINAR-020 / S2): a warrior with the
    /// starter sword, level from total EXP (max 99), talary balance. Shared by the AR encounter (rewards) and the
    /// world-map HUD (level, 4 × 25% EXP orbs, talary). In memory only: nothing is written to disk or sent anywhere;
    /// saving and server authority come with S11 (#56) / #39.
    /// </summary>
    public static class LocalPlayerSession
    {
        private static BalanceConfig _config;
        private static ExpCurve _curve;
        private static PlayerProgress _progress;

        /// <summary>Raised after EXP/talary changed (e.g. a mob was defeated).</summary>
        public static event Action Changed;

        public static BalanceConfig Config
        {
            get
            {
                if (_config == null)
                {
                    _config = BalanceConfig.CreateDefault();
                }

                return _config;
            }
        }

        public static ExpCurve Curve
        {
            get
            {
                if (_curve == null)
                {
                    _curve = new ExpCurve(Config);
                }

                return _curve;
            }
        }

        public static PlayerProgress Progress
        {
            get
            {
                if (_progress == null)
                {
                    _progress = new PlayerProgress(Curve, ClassId.Warrior);
                }

                return _progress;
            }
        }

        public static void NotifyChanged()
        {
            Changed?.Invoke();
        }

        /// <summary>Fresh level-1 warrior (tests; also runs on every Play Mode start without domain reload).</summary>
        public static void Reset()
        {
            _progress = null;
            NotifyChanged();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            _progress = null;
            Changed = null;
        }
    }
}
