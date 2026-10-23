using MetinAR.Audio;
using Xunit;

namespace MetinAR.Audio.Tests
{
    public class AudioCueTableTests
    {
        public static IEnumerable<object[]> AllEvents() =>
            Enum.GetValues(typeof(GameAudioEvent)).Cast<GameAudioEvent>().Select(e => new object[] { e });

        [Theory]
        [MemberData(nameof(AllEvents))]
        public void EveryEventHasExactlyOneCueWithClips(GameAudioEvent e)
        {
            Assert.Single(AudioCueTable.All, c => c.Event == e);
            AudioCue cue = AudioCueTable.Get(e);
            Assert.NotEmpty(cue.Clips);
            Assert.All(cue.Clips, id => Assert.StartsWith("SFX/", id));
        }

        [Theory]
        [MemberData(nameof(AllEvents))]
        public void EachEventPlaysExactlyOneClipOfItsCueOnItsChannel(GameAudioEvent e)
        {
            var output = new RecordingOutput();
            double now = 100;
            var director = new AudioEventDirector(output, new CountingHaptics(), new AudioSettingsModel(), () => now);
            Assert.True(director.Handle(e));
            var play = Assert.Single(output.Plays);
            AudioCue cue = AudioCueTable.Get(e);
            Assert.Contains(play.Clip, cue.Clips);
            Assert.Equal(cue.Channel, play.Channel);
            Assert.InRange(play.Pitch, cue.PitchMin, cue.PitchMax);
            Assert.Equal(cue.Volume, play.Volume);
        }

        [Theory]
        [InlineData(GameAudioEvent.SwordSwing, AudioCueTable.SwordSwing)]
        [InlineData(GameAudioEvent.SwordHit, AudioCueTable.SwordHit)]
        [InlineData(GameAudioEvent.ShieldBlock, AudioCueTable.ShieldBlock)]
        [InlineData(GameAudioEvent.AuraStart, AudioCueTable.AuraStart)]
        [InlineData(GameAudioEvent.AuraStop, AudioCueTable.AuraStop)]
        [InlineData(GameAudioEvent.MobAttack, AudioCueTable.SpiderAttack)]
        [InlineData(GameAudioEvent.MobHurt, AudioCueTable.SpiderHit)]
        [InlineData(GameAudioEvent.MobDeath, AudioCueTable.SpiderDeath)]
        [InlineData(GameAudioEvent.RewardCoins, AudioCueTable.CoinPickup)]
        [InlineData(GameAudioEvent.LootPickup, AudioCueTable.LootCommon)]
        [InlineData(GameAudioEvent.LevelUp, AudioCueTable.LevelUp)]
        [InlineData(GameAudioEvent.Purchase, AudioCueTable.Buy)]
        [InlineData(GameAudioEvent.Sell, AudioCueTable.Sell)]
        [InlineData(GameAudioEvent.ShopOpen, AudioCueTable.ShopOpen)]
        [InlineData(GameAudioEvent.UiClick, AudioCueTable.Click)]
        [InlineData(GameAudioEvent.UiDenied, AudioCueTable.Denied)]
        [InlineData(GameAudioEvent.PoisonTick, AudioCueTable.PoisonTick)]
        [InlineData(GameAudioEvent.PotionDrink, AudioCueTable.Potion)]
        [InlineData(GameAudioEvent.ItemEquipMetal, AudioCueTable.EquipMetal)]
        public void EventMapsToSpecClip(GameAudioEvent e, string clip)
        {
            Assert.Equal(new[] { clip }, AudioCueTable.Get(e).Clips);
        }

        [Fact]
        public void UiEventsUseUiGroupAndCombatUsesSfx()
        {
            foreach (var e in new[] { GameAudioEvent.UiClick, GameAudioEvent.UiDenied, GameAudioEvent.Purchase, GameAudioEvent.Sell, GameAudioEvent.ShopOpen })
                Assert.Equal(AudioChannel.Ui, AudioCueTable.Get(e).Channel);
            foreach (var e in new[] { GameAudioEvent.SwordSwing, GameAudioEvent.SwordHit, GameAudioEvent.MobDeath, GameAudioEvent.PlayerHurt })
                Assert.Equal(AudioChannel.Sfx, AudioCueTable.Get(e).Channel);
            Assert.DoesNotContain(AudioCueTable.All, c => c.Channel == AudioChannel.Music); // #68 owns music.
        }

        [Fact]
        public void HapticsOnlyOnHitAndDamage()
        {
            var haptic = AudioCueTable.All.Where(c => c.Haptic).Select(c => c.Event).OrderBy(e => e).ToArray();
            Assert.Equal(new[] { GameAudioEvent.SwordHit, GameAudioEvent.SwordCrit, GameAudioEvent.ShieldBlock, GameAudioEvent.PlayerHurt }.OrderBy(e => e), haptic);
        }

        [Fact]
        public void AuraLoopClipIsIncluded()
        {
            Assert.Equal(AudioCueTable.AuraLoop, AudioCueTable.LoopClip(GameAudioLoop.Aura));
            Assert.Contains(AudioCueTable.AuraLoop, AudioCueTable.AllClipIds());
            Assert.Equal(AudioCueTable.AllClipIds().Count, AudioCueTable.AllClipIds().Distinct().Count());
        }
    }
}
