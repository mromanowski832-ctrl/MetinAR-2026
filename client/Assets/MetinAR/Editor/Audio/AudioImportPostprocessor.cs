using UnityEditor;
using UnityEngine;

namespace MetinAR.Editor.Audio
{
    /// <summary>
    /// METINAR-018: applies the mobile audio import profile (AudioPipelineRules.GetProfile) to NEW audio files under
    /// Assets/MetinAR/Audio only, on their first import (no import settings yet), as the METINAR-009 model/texture
    /// postprocessor does. Committed .meta files already carry the same values, so existing clips are not rewritten
    /// and deliberate Inspector overrides made later are kept. Nothing outside Assets/MetinAR/Audio is touched.
    /// </summary>
    public sealed class AudioImportPostprocessor : AssetPostprocessor
    {
        public const string AndroidPlatform = "Android";
        public const string IosPlatform = "iOS";

        private void OnPreprocessAudio()
        {
            if (!AudioPipelineRules.IsPipelinePath(assetPath) || !assetImporter.importSettingsMissing)
            {
                return;
            }

            var importer = assetImporter as AudioImporter;
            if (importer == null)
            {
                return;
            }

            AudioImportProfile profile = AudioPipelineRules.GetProfile(AudioPipelineRules.ClassifyPath(assetPath));
            if (profile == null)
            {
                Debug.LogWarning("[MetinAR Audio] " + assetPath + " is outside the audio pipeline folders; import settings left at Unity defaults.");
                return;
            }

            importer.forceToMono = profile.ForceToMono;
            importer.loadInBackground = profile.LoadInBackground;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = (AudioClipLoadType)profile.LoadType;
            settings.compressionFormat = (AudioCompressionFormat)profile.CompressionFormat;
            settings.quality = profile.Quality;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = profile.PreloadAudioData;
            importer.defaultSampleSettings = settings;
            importer.SetOverrideSampleSettings(AndroidPlatform, settings);
            importer.SetOverrideSampleSettings(IosPlatform, settings);
        }
    }
}
