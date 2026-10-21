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

            // Peak levels are set when the sound is made (LICENSES.md / docs); Unity's Normalize would undo them.
            // AudioImporter has no public property for it, so the serialized field is set; if a future Unity renames
            // it, the warning below fires and tests/AssetPipeline.Tests still rejects "normalize: 1" in the .meta.
            var serialized = new SerializedObject(importer);
            SerializedProperty normalize = serialized.FindProperty("m_Normalize");
            if (normalize != null)
            {
                normalize.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[MetinAR Audio] " + assetPath + ": could not clear Normalize (m_Normalize not found); untick it in the Inspector.");
            }
        }
    }
}
