using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace MetinAR.Editor.Audio
{
    /// <summary>
    /// METINAR-018: creates the project AudioMixer (Master with child groups SFX, UI, Music) without manual clicks.
    /// Unity has no public API to create a .mixer asset, so this uses the editor-internal AudioMixerController by
    /// reflection (the same calls as Assets > Create > Audio Mixer and "Add child group"). It only runs on demand:
    /// Unity.exe -batchmode -nographics -projectPath client -executeMethod MetinAR.Editor.Audio.AudioMixerBuilder.BuildBatch
    /// If the asset already exists it is not touched. If a future Unity changes the internal API the builder fails with
    /// a clear log line and exit code 1; the committed .mixer keeps working (verification uses only public API).
    /// </summary>
    public static class AudioMixerBuilder
    {
        public const string MixerFolder = AudioPipelineRules.Root + "/Mixers";
        public const string MixerPath = MixerFolder + "/MetinARMixer.mixer";
        public const string MasterGroup = "Master";
        public static readonly string[] ChildGroups = { "SFX", "UI", "Music" };

        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static void BuildBatch()
        {
            bool ok;
            try
            {
                ok = Build();
            }
            catch (Exception e)
            {
                Debug.LogError("[AudioMixerBuilder] FAIL " + e);
                ok = false;
            }

            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Build()
        {
            if (AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath) != null)
            {
                Debug.Log("[AudioMixerBuilder] exists, not modified: " + MixerPath);
                return true;
            }

            if (!AssetDatabase.IsValidFolder(MixerFolder))
            {
                AssetDatabase.CreateFolder(AudioPipelineRules.Root, "Mixers");
            }

            Assembly editor = typeof(UnityEditor.Editor).Assembly;
            Type controllerType = editor.GetType("UnityEditor.Audio.AudioMixerController");
            Type groupType = editor.GetType("UnityEditor.Audio.AudioMixerGroupController");
            if (controllerType == null || groupType == null)
            {
                Debug.LogError("[AudioMixerBuilder] FAIL internal types not found (AudioMixerController / AudioMixerGroupController)");
                return false;
            }

            MethodInfo create = controllerType.GetMethod("CreateMixerControllerAtPath", Any, null, new[] { typeof(string) }, null);
            MethodInfo createGroup = controllerType.GetMethod("CreateNewGroup", Any, null, new[] { typeof(string), typeof(bool) }, null);
            MethodInfo addChild = controllerType.GetMethod("AddChildToParent", Any, null, new[] { groupType, groupType }, null);
            PropertyInfo master = controllerType.GetProperty("masterGroup", Any);
            if (create == null || createGroup == null || addChild == null || master == null)
            {
                Debug.LogError("[AudioMixerBuilder] FAIL internal API changed: create=" + (create != null) + " createGroup=" + (createGroup != null) +
                               " addChild=" + (addChild != null) + " masterGroup=" + (master != null));
                return false;
            }

            object controller = create.Invoke(null, new object[] { MixerPath });
            object masterGroup = master.GetValue(controller, null);
            if (controller == null || masterGroup == null)
            {
                Debug.LogError("[AudioMixerBuilder] FAIL controller or master group is null");
                return false;
            }

            foreach (string name in ChildGroups)
            {
                object group = createGroup.Invoke(controller, new object[] { name, false });
                addChild.Invoke(controller, new[] { group, masterGroup });
            }

            EditorUtility.SetDirty((UnityEngine.Object)controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MixerPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[AudioMixerBuilder] created " + MixerPath + " with " + MasterGroup + "/{" + string.Join(",", ChildGroups) + "}");
            return true;
        }
    }
}
