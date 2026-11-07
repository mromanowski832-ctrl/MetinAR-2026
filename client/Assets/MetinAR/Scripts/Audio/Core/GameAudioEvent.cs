namespace MetinAR.Audio
{
    /// <summary>Mixer group a cue is routed to. Each channel has its own volume (exposed mixer parameter).</summary>
    public enum AudioChannel
    {
        Sfx = 0,
        Ui = 1,
        Music = 2,
    }

    /// <summary>
    /// METINAR-040 (#85): game events that make a sound. Spec 2026-10-23 §13 and COMBAT_PHYSICS.md §5.
    /// Every value has exactly one cue in <see cref="AudioCueTable"/> (checked by tests/MetinAR.Audio.Tests).
    /// Gameplay code raises them with <see cref="GameAudio.Raise"/>; it never touches AudioSource directly.
    /// </summary>
    public enum GameAudioEvent
    {
        // Player weapon (FPP combat).
        SwordSwing = 0,
        SwordHit = 1,
        SwordCrit = 2,
        SwordMiss = 3,
        ShieldBlock = 4,
        AuraStart = 5,
        AuraStop = 6,

        // Monsters.
        MobAppear = 10,
        MobAttack = 11,
        MobHurt = 12,
        MobDeath = 13,

        // Player status.
        PlayerHurt = 20,
        PoisonTick = 21,

        // Rewards and progression.
        RewardCoins = 30,
        LootPickup = 31,
        LevelUp = 32,

        // Items.
        ItemEquipMetal = 40,
        ItemUnequipMetal = 41,
        ItemEquipLeather = 42,
        ItemUnequipLeather = 43,
        PotionDrink = 44,
        AntidoteDrink = 45,

        // Merchant and smith.
        ShopOpen = 50,
        Purchase = 51,
        Sell = 52,
        UpgradeSuccess = 53,
        UpgradeFail = 54,

        // Interface.
        UiClick = 60,
        UiDenied = 61,
        ArEnter = 62,
    }

    /// <summary>Sustained sounds that run while a state lasts (one looping voice each).</summary>
    public enum GameAudioLoop
    {
        Aura = 0,
    }
}
