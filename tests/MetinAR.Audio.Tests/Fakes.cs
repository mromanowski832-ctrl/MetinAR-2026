using MetinAR.Audio;

namespace MetinAR.Audio.Tests
{
    internal sealed class RecordingOutput : IAudioOutput
    {
        public readonly List<(string Clip, AudioChannel Channel, float Volume, float Pitch)> Plays = new();

        public void PlayOneShot(string clipId, AudioChannel channel, float volume, float pitch) =>
            Plays.Add((clipId, channel, volume, pitch));
    }

    internal sealed class CountingHaptics : IHapticsOutput
    {
        public int Calls;
        public void Vibrate() => Calls++;
    }

    internal sealed class MemoryStore : IAudioSettingsStore
    {
        public readonly Dictionary<string, float> Floats = new();
        public readonly Dictionary<string, int> Ints = new();
        public int Saves;

        public bool TryGetFloat(string key, out float value) => Floats.TryGetValue(key, out value);
        public void SetFloat(string key, float value) => Floats[key] = value;
        public bool TryGetInt(string key, out int value) => Ints.TryGetValue(key, out value);
        public void SetInt(string key, int value) => Ints[key] = value;
        public void Save() => Saves++;
    }

    internal static class Repo
    {
        public static string Root()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "client", "Assets"))) dir = Path.GetDirectoryName(dir);
            Xunit.Assert.NotNull(dir);
            return dir;
        }

        public static string MetinArRoot() => Path.Combine(Root(), "client", "Assets", "MetinAR");
        public static string AudioRoot() => Path.Combine(MetinArRoot(), "Audio");

        public static string Guid(string metaPath)
        {
            foreach (string line in File.ReadAllLines(metaPath))
                if (line.StartsWith("guid: ", StringComparison.Ordinal)) return line.Substring(6).Trim();
            throw new InvalidOperationException("no guid in " + metaPath);
        }
    }
}
