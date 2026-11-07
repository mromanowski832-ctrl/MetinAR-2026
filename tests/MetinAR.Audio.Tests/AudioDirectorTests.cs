using MetinAR.Audio;
using Xunit;

namespace MetinAR.Audio.Tests
{
    public class AudioDirectorTests
    {
        private double _now = 10;
        private readonly RecordingOutput _out = new();
        private readonly CountingHaptics _haptics = new();

        private AudioEventDirector Director(AudioSettingsModel settings = null, int seed = 85) =>
            new AudioEventDirector(_out, _haptics, settings ?? new AudioSettingsModel(), () => _now, seed);

        [Fact]
        public void AntiSpamDropsRepeatInsideIntervalAndAllowsAfter()
        {
            var d = Director();
            float interval = AudioCueTable.Get(GameAudioEvent.SwordSwing).MinIntervalSeconds;
            Assert.True(d.Handle(GameAudioEvent.SwordSwing));
            _now += interval * 0.5;
            Assert.False(d.Handle(GameAudioEvent.SwordSwing));
            _now += interval;
            Assert.True(d.Handle(GameAudioEvent.SwordSwing));
            Assert.Equal(2, _out.Plays.Count);
            Assert.Equal(1, d.SuppressedCount);
        }

        [Fact]
        public void DifferentEventsAtTheSameInstantAllPlay()
        {
            var d = Director();
            Assert.True(d.Handle(GameAudioEvent.SwordHit));
            Assert.True(d.Handle(GameAudioEvent.MobDeath));
            Assert.True(d.Handle(GameAudioEvent.RewardCoins));
            Assert.Equal(3, _out.Plays.Count);
        }

        [Fact]
        public void VariantsNeverRepeatImmediately()
        {
            var d = Director();
            string previous = null;
            var seen = new HashSet<string>();
            for (int i = 0; i < 200; i++)
            {
                _now += 1;
                Assert.True(d.Handle(GameAudioEvent.MobAppear));
                string clip = _out.Plays[^1].Clip;
                Assert.NotEqual(previous, clip);
                previous = clip;
                seen.Add(clip);
            }

            Assert.Equal(3, seen.Count);
        }

        [Fact]
        public void PitchVariesWithinCueRange()
        {
            var d = Director();
            for (int i = 0; i < 50; i++)
            {
                _now += 1;
                d.Handle(GameAudioEvent.SwordHit);
            }

            var pitches = _out.Plays.Select(p => p.Pitch).ToList();
            Assert.True(pitches.Distinct().Count() > 10);
            AudioCue cue = AudioCueTable.Get(GameAudioEvent.SwordHit);
            Assert.All(pitches, p => Assert.InRange(p, cue.PitchMin, cue.PitchMax));
        }

        [Fact]
        public void VibrationOffNeverCallsTheDevice()
        {
            var settings = new AudioSettingsModel { HapticsEnabled = false };
            var d = Director(settings);
            foreach (GameAudioEvent e in Enum.GetValues(typeof(GameAudioEvent)))
            {
                _now += 10;
                d.Handle(e);
            }

            Assert.Equal(0, _haptics.Calls);
            Assert.Equal(0, d.HapticCount);
        }

        [Fact]
        public void VibrationOnBuzzesOnHitAndDamageOnly()
        {
            var d = Director();
            d.Handle(GameAudioEvent.UiClick);
            d.Handle(GameAudioEvent.SwordSwing);
            Assert.Equal(0, _haptics.Calls);
            _now += 1;
            d.Handle(GameAudioEvent.SwordHit);
            Assert.Equal(1, _haptics.Calls);
            _now += 1;
            d.Handle(GameAudioEvent.PlayerHurt);
            Assert.Equal(2, _haptics.Calls);
        }

        [Fact]
        public void VibrationIsRateLimited()
        {
            var d = Director();
            d.Handle(GameAudioEvent.SwordHit);
            _now += 0.1;
            d.Handle(GameAudioEvent.PlayerHurt);
            Assert.Equal(1, _haptics.Calls);
            _now += AudioEventDirector.HapticMinIntervalSeconds;
            d.Handle(GameAudioEvent.ShieldBlock);
            Assert.Equal(2, _haptics.Calls);
        }

        [Fact]
        public void ChannelAtZeroStartsNoVoiceButOtherChannelsPlay()
        {
            var settings = new AudioSettingsModel();
            settings.SetVolume(AudioChannel.Sfx, 0f);
            var d = Director(settings);
            Assert.False(d.Handle(GameAudioEvent.SwordHit));
            Assert.True(d.Handle(GameAudioEvent.UiClick));
            Assert.Single(_out.Plays);
            Assert.Equal(AudioChannel.Ui, _out.Plays[0].Channel);
            Assert.Equal(1, _haptics.Calls); // vibration is independent of volume.
        }

        [Fact]
        public void MasterZeroSilencesEverything()
        {
            var settings = new AudioSettingsModel { Master = 0f };
            var d = Director(settings);
            foreach (GameAudioEvent e in Enum.GetValues(typeof(GameAudioEvent)))
            {
                _now += 10;
                d.Handle(e);
            }

            Assert.Empty(_out.Plays);
        }

        [Fact]
        public void ResetHistoryClearsAntiSpam()
        {
            var d = Director();
            d.Handle(GameAudioEvent.LevelUp);
            Assert.False(d.Handle(GameAudioEvent.LevelUp));
            d.ResetHistory();
            Assert.True(d.Handle(GameAudioEvent.LevelUp));
        }

        [Fact]
        public void BusRaisesToSubscribers()
        {
            var got = new List<GameAudioEvent>();
            var loops = new List<(GameAudioLoop, bool)>();
            Action<GameAudioEvent> h = got.Add;
            Action<GameAudioLoop, bool> l = (a, b) => loops.Add((a, b));
            GameAudio.Raised += h;
            GameAudio.LoopChanged += l;
            try
            {
                GameAudio.Raise(GameAudioEvent.ShieldBlock);
                GameAudio.SetLoop(GameAudioLoop.Aura, true);
            }
            finally
            {
                GameAudio.Raised -= h;
                GameAudio.LoopChanged -= l;
            }

            Assert.Equal(new[] { GameAudioEvent.ShieldBlock }, got);
            Assert.Equal(new[] { (GameAudioLoop.Aura, true) }, loops);
            GameAudio.Raise(GameAudioEvent.UiClick); // no listener: no-op, no throw.
        }
    }
}
