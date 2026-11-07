using System;

namespace MetinAR.Audio
{
    /// <summary>
    /// METINAR-040 (#85): the audio event bus. Gameplay code that has a sound moment calls one line, e.g.
    /// <c>GameAudio.Raise(GameAudioEvent.ShieldBlock);</c> — no reference to AudioService, AudioSource or clips.
    /// Without a listener (tests, headless tools) a raise is a no-op.
    /// </summary>
    public static class GameAudio
    {
        public static event Action<GameAudioEvent> Raised;

        public static event Action<GameAudioLoop, bool> LoopChanged;

        public static void Raise(GameAudioEvent gameEvent) => Raised?.Invoke(gameEvent);

        public static void SetLoop(GameAudioLoop loop, bool playing) => LoopChanged?.Invoke(loop, playing);

        /// <summary>Test helper: drop all listeners.</summary>
        public static void ClearListeners()
        {
            Raised = null;
            LoopChanged = null;
        }
    }
}
