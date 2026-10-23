using System;
using System.Collections.Generic;

namespace MetinAR.Audio
{
    /// <summary>
    /// METINAR-040 (#85): the event → clip table. Only clips registered in client/Assets/MetinAR/Audio/LICENSES.md
    /// (all MetinAR-Owned procedural SFX from #43/#62). Entries marked "placeholder" reuse an existing clip until the
    /// dedicated clip from COMBAT_PHYSICS.md §5 exists (Whiff, Crit, Smith_Temper*, AR stinger).
    /// </summary>
    public static class AudioCueTable
    {
        public const string SwordSwing = "SFX/Combat/SFX_Combat_SwordSwing_01.wav";
        public const string SwordHit = "SFX/Combat/SFX_Combat_SwordHit_01.wav";
        public const string ShieldBlock = "SFX/Combat/SFX_Combat_ShieldBlock_01.wav";
        public const string AuraStart = "SFX/Combat/SFX_Combat_AuraStart_01.wav";
        public const string AuraLoop = "SFX/Combat/SFX_Combat_AuraLoop_01.wav";
        public const string AuraStop = "SFX/Combat/SFX_Combat_AuraStop_01.wav";
        public const string SpiderAttack = "SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Attack_01.wav";
        public const string SpiderHit = "SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Hit_01.wav";
        public const string SpiderDeath = "SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Death_01.wav";
        public const string SpiderStep1 = "SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_01.wav";
        public const string SpiderStep2 = "SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_02.wav";
        public const string SpiderStep3 = "SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_03.wav";
        public const string PoisonTick = "SFX/Status/SFX_Status_PoisonTick_01.wav";
        public const string CoinPickup = "SFX/UI/SFX_UI_CoinPickup_01.wav";
        public const string LootCommon = "SFX/Loot/SFX_Loot_Common_01.wav";
        public const string LevelUp = "SFX/Stinger/SFX_Stinger_LevelUp_01.wav";
        public const string EquipMetal = "SFX/Item/SFX_Item_EquipMetal_01.wav";
        public const string UnequipMetal = "SFX/Item/SFX_Item_UnequipMetal_01.wav";
        public const string EquipLeather = "SFX/Item/SFX_Item_EquipLeather_01.wav";
        public const string UnequipLeather = "SFX/Item/SFX_Item_UnequipLeather_01.wav";
        public const string Potion = "SFX/Item/SFX_Item_Potion_01.wav";
        public const string Antidote = "SFX/Item/SFX_Item_Antidote_01.wav";
        public const string ShopOpen = "SFX/UI/SFX_UI_ShopOpen_01.wav";
        public const string Buy = "SFX/UI/SFX_UI_Buy_01.wav";
        public const string Sell = "SFX/UI/SFX_UI_Sell_01.wav";
        public const string Click = "SFX/UI/SFX_UI_Click_01.wav";
        public const string Denied = "SFX/UI/SFX_UI_Denied_01.wav";

        private static readonly AudioCue[] Cues =
        {
            // Combat — SFX group. Pitch spread ±4–6 % so repeated swings do not sound machine-gunned.
            new AudioCue(GameAudioEvent.SwordSwing, AudioChannel.Sfx, new[] { SwordSwing }, 0.85f, 0.95f, 1.05f, 0.12f),
            new AudioCue(GameAudioEvent.SwordHit, AudioChannel.Sfx, new[] { SwordHit }, 1f, 0.95f, 1.05f, 0.06f, haptic: true),
            // Placeholder until SFX_Combat_Crit_01: the hit clip, pitched down and full level.
            new AudioCue(GameAudioEvent.SwordCrit, AudioChannel.Sfx, new[] { SwordHit }, 1f, 0.82f, 0.86f, 0.06f, haptic: true),
            // Placeholder until SFX_Combat_Whiff_01: a lighter, higher swing.
            new AudioCue(GameAudioEvent.SwordMiss, AudioChannel.Sfx, new[] { SwordSwing }, 0.55f, 1.18f, 1.24f, 0.12f),
            new AudioCue(GameAudioEvent.ShieldBlock, AudioChannel.Sfx, new[] { ShieldBlock }, 1f, 0.96f, 1.04f, 0.08f, haptic: true),
            new AudioCue(GameAudioEvent.AuraStart, AudioChannel.Sfx, new[] { AuraStart }, 0.9f, 1f, 1f, 0.5f),
            new AudioCue(GameAudioEvent.AuraStop, AudioChannel.Sfx, new[] { AuraStop }, 0.8f, 1f, 1f, 0.5f),

            new AudioCue(GameAudioEvent.MobAppear, AudioChannel.Sfx, new[] { SpiderStep1, SpiderStep2, SpiderStep3 }, 0.8f, 0.94f, 1.06f, 0.15f),
            new AudioCue(GameAudioEvent.MobAttack, AudioChannel.Sfx, new[] { SpiderAttack }, 0.9f, 0.95f, 1.05f, 0.25f),
            new AudioCue(GameAudioEvent.MobHurt, AudioChannel.Sfx, new[] { SpiderHit }, 0.8f, 0.94f, 1.06f, 0.08f),
            new AudioCue(GameAudioEvent.MobDeath, AudioChannel.Sfx, new[] { SpiderDeath }, 1f, 1f, 1f, 0.5f),

            // Placeholder until a dedicated player-hurt clip: the sword impact, low and dull.
            new AudioCue(GameAudioEvent.PlayerHurt, AudioChannel.Sfx, new[] { SwordHit }, 0.75f, 0.68f, 0.72f, 0.2f, haptic: true),
            new AudioCue(GameAudioEvent.PoisonTick, AudioChannel.Sfx, new[] { PoisonTick }, 0.7f, 0.97f, 1.03f, 0.3f),

            new AudioCue(GameAudioEvent.RewardCoins, AudioChannel.Sfx, new[] { CoinPickup }, 0.9f, 0.98f, 1.02f, 0.1f),
            new AudioCue(GameAudioEvent.LootPickup, AudioChannel.Sfx, new[] { LootCommon }, 0.9f, 0.98f, 1.02f, 0.1f),
            new AudioCue(GameAudioEvent.LevelUp, AudioChannel.Sfx, new[] { LevelUp }, 1f, 1f, 1f, 1f),

            new AudioCue(GameAudioEvent.ItemEquipMetal, AudioChannel.Sfx, new[] { EquipMetal }, 0.85f, 0.98f, 1.02f, 0.1f),
            new AudioCue(GameAudioEvent.ItemUnequipMetal, AudioChannel.Sfx, new[] { UnequipMetal }, 0.85f, 0.98f, 1.02f, 0.1f),
            new AudioCue(GameAudioEvent.ItemEquipLeather, AudioChannel.Sfx, new[] { EquipLeather }, 0.85f, 0.98f, 1.02f, 0.1f),
            new AudioCue(GameAudioEvent.ItemUnequipLeather, AudioChannel.Sfx, new[] { UnequipLeather }, 0.85f, 0.98f, 1.02f, 0.1f),
            new AudioCue(GameAudioEvent.PotionDrink, AudioChannel.Sfx, new[] { Potion }, 0.85f, 1f, 1f, 0.3f),
            new AudioCue(GameAudioEvent.AntidoteDrink, AudioChannel.Sfx, new[] { Antidote }, 0.85f, 1f, 1f, 0.3f),

            // Merchant / smith — UI group.
            new AudioCue(GameAudioEvent.ShopOpen, AudioChannel.Ui, new[] { ShopOpen }, 0.8f, 1f, 1f, 0.3f),
            new AudioCue(GameAudioEvent.Purchase, AudioChannel.Ui, new[] { Buy }, 0.9f, 1f, 1f, 0.1f),
            new AudioCue(GameAudioEvent.Sell, AudioChannel.Ui, new[] { Sell }, 0.9f, 1f, 1f, 0.1f),
            // Placeholders until SFX_Smith_TemperSuccess_01 / TemperFail_01 (#54).
            new AudioCue(GameAudioEvent.UpgradeSuccess, AudioChannel.Ui, new[] { Buy }, 0.9f, 1.1f, 1.1f, 0.3f),
            new AudioCue(GameAudioEvent.UpgradeFail, AudioChannel.Ui, new[] { Denied }, 0.9f, 0.9f, 0.9f, 0.3f),

            new AudioCue(GameAudioEvent.UiClick, AudioChannel.Ui, new[] { Click }, 0.7f, 0.97f, 1.03f, 0.05f),
            new AudioCue(GameAudioEvent.UiDenied, AudioChannel.Ui, new[] { Denied }, 0.8f, 1f, 1f, 0.2f),
            // Placeholder until an AR-enter stinger: the soft "opening" chime.
            new AudioCue(GameAudioEvent.ArEnter, AudioChannel.Ui, new[] { ShopOpen }, 0.7f, 0.9f, 0.9f, 1f),
        };

        private static readonly Dictionary<GameAudioEvent, AudioCue> ByEvent = BuildIndex();

        public static IReadOnlyList<AudioCue> All => Cues;

        /// <summary>The looping clip of <paramref name="loop"/>.</summary>
        public static string LoopClip(GameAudioLoop loop)
        {
            switch (loop)
            {
                case GameAudioLoop.Aura: return AuraLoop;
                default: throw new ArgumentOutOfRangeException(nameof(loop), loop, null);
            }
        }

        public static float LoopVolume(GameAudioLoop loop) => loop == GameAudioLoop.Aura ? 0.5f : 1f;

        public static bool TryGet(GameAudioEvent gameEvent, out AudioCue cue) => ByEvent.TryGetValue(gameEvent, out cue);

        public static AudioCue Get(GameAudioEvent gameEvent)
        {
            if (!ByEvent.TryGetValue(gameEvent, out AudioCue cue))
                throw new KeyNotFoundException("No audio cue for " + gameEvent);
            return cue;
        }

        /// <summary>Every clip id the game can play (cues + loops), without duplicates, in table order.</summary>
        public static List<string> AllClipIds()
        {
            var ids = new List<string>();
            foreach (AudioCue cue in Cues)
                foreach (string clip in cue.Clips)
                    if (!ids.Contains(clip)) ids.Add(clip);
            foreach (GameAudioLoop loop in Enum.GetValues(typeof(GameAudioLoop)))
            {
                string clip = LoopClip(loop);
                if (!ids.Contains(clip)) ids.Add(clip);
            }

            return ids;
        }

        private static Dictionary<GameAudioEvent, AudioCue> BuildIndex()
        {
            var map = new Dictionary<GameAudioEvent, AudioCue>();
            foreach (AudioCue cue in Cues)
            {
                if (map.ContainsKey(cue.Event)) throw new InvalidOperationException("Duplicate audio cue for " + cue.Event);
                map.Add(cue.Event, cue);
            }

            return map;
        }
    }
}
