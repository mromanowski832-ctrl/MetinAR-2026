namespace MetinAR.Gameplay
{
    /// <summary>Which offensive stat feeds the hit and which Def rule applies (COMBAT_PHYSICS.md §1, steps 3 and 6).</summary>
    public enum DamageKind
    {
        /// <summary>Uses Atk; full Def.</summary>
        Physical = 0,
        /// <summary>Uses Mag; half Def plus the resistance of the damage type.</summary>
        Magical = 1,
    }

    /// <summary>Damage types (CLASSES_SKILLS.md §2): physical, elemental magic (szaman), dark (Slajter), poison (DoT).</summary>
    public enum DamageType
    {
        Physical = 0,
        Elemental = 1,
        Dark = 2,
        Poison = 3,
    }
}
