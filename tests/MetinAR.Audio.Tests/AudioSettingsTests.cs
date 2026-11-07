using MetinAR.Audio;
using Xunit;

namespace MetinAR.Audio.Tests
{
    public class AudioSettingsTests
    {
        [Fact]
        public void VolumeZeroMutesTheGroupAtMinus80Db()
        {
            Assert.Equal(-80f, AudioSettingsModel.ToDecibels(0f));
            Assert.Equal(-80f, AudioSettingsModel.ToDecibels(-1f));
            Assert.Equal(-80f, AudioSettingsModel.ToDecibels(float.NaN));
            Assert.Equal(0f, AudioSettingsModel.ToDecibels(1f));
            Assert.InRange(AudioSettingsModel.ToDecibels(0.5f), -6.03f, -6.0f);
            Assert.InRange(AudioSettingsModel.ToDecibels(0.0002f), -80f, -73f);
        }

        [Fact]
        public void ChannelsAreIndependentAndMapToExposedParameters()
        {
            var s = new AudioSettingsModel();
            s.SetVolume(AudioChannel.Sfx, 0.2f);
            s.SetVolume(AudioChannel.Music, 0f);
            s.SetVolume(AudioChannel.Ui, 0.9f);
            Assert.Equal(0.2f, s.GetVolume(AudioChannel.Sfx));
            Assert.Equal(0f, s.GetVolume(AudioChannel.Music));
            Assert.Equal(0.9f, s.GetVolume(AudioChannel.Ui));
            Assert.False(s.IsAudible(AudioChannel.Music));
            Assert.True(s.IsAudible(AudioChannel.Sfx));
            Assert.Equal("SfxVolume", AudioSettingsModel.MixerParameter(AudioChannel.Sfx));
            Assert.Equal("UiVolume", AudioSettingsModel.MixerParameter(AudioChannel.Ui));
            Assert.Equal("MusicVolume", AudioSettingsModel.MixerParameter(AudioChannel.Music));
        }

        [Fact]
        public void ValuesAreClampedAndPersistedAndReloaded()
        {
            var store = new MemoryStore();
            var s = new AudioSettingsModel(store);
            int changes = 0;
            s.Changed += () => changes++;
            s.SetVolume(AudioChannel.Sfx, 1.7f);  // default is already 1 → clamped, no change
            s.SetVolume(AudioChannel.Ui, -3f);
            s.SetVolume(AudioChannel.Music, 0.25f);
            s.HapticsEnabled = false;
            Assert.Equal(3, changes);
            Assert.Equal(0f, store.Floats[AudioSettingsModel.UiKey]);
            Assert.Equal(0.25f, store.Floats[AudioSettingsModel.MusicKey]);
            Assert.Equal(0, store.Ints[AudioSettingsModel.HapticsKey]);
            Assert.True(store.Saves >= 3);

            var reloaded = new AudioSettingsModel(store);
            Assert.Equal(1f, reloaded.GetVolume(AudioChannel.Sfx));
            Assert.Equal(0f, reloaded.GetVolume(AudioChannel.Ui));
            Assert.Equal(0.25f, reloaded.GetVolume(AudioChannel.Music));
            Assert.False(reloaded.HapticsEnabled);
        }

        [Fact]
        public void DefaultsWhenStoreIsEmptyOrCorrupt()
        {
            var store = new MemoryStore();
            store.Floats[AudioSettingsModel.SfxKey] = float.NaN;
            var s = new AudioSettingsModel(store);
            Assert.Equal(AudioSettingsModel.DefaultSfx, s.GetVolume(AudioChannel.Sfx));
            Assert.Equal(AudioSettingsModel.DefaultUi, s.GetVolume(AudioChannel.Ui));
            Assert.Equal(AudioSettingsModel.DefaultMusic, s.GetVolume(AudioChannel.Music));
            Assert.True(s.HapticsEnabled);
        }
    }
}
