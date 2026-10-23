using System;

namespace MetinAR.Gameplay
{
    /// <summary>Random source for gameplay rolls. Implementations return values in [0, 1).</summary>
    public interface IGameplayRandom
    {
        double NextDouble();
    }

    /// <summary>
    /// Deterministic, platform-independent seeded generator (SplitMix64), so a seed reproduces the same rolls
    /// in tests, in the Unity client (Mono/IL2CPP) and on a future server (COMBAT_PHYSICS.md §3: deterministic).
    /// </summary>
    public sealed class SeededGameplayRandom : IGameplayRandom
    {
        private ulong _state;

        public SeededGameplayRandom(ulong seed)
        {
            _state = seed;
        }

        public ulong NextULong()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public double NextDouble()
        {
            // 53 random bits → [0, 1).
            return (NextULong() >> 11) * (1.0 / 9007199254740992.0);
        }
    }
}
