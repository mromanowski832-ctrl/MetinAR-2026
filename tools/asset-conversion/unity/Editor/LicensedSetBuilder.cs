// METINAR-IMPORT-001 (#35) — preview-project builder for converted sets.
// STATUS: NOT_RUN in Unity yet. Intended ONLY for a separate preview project
// (Unity 6000.6.4f1 + URP), never for the production client/ project.
//
// Input folder (per docs/METINAR-009-ASSET-PIPELINE.md section 2):
//   Assets/MetinAR/Characters/<Mobs|Bosses|NPC>/<Id>/
//     Models/SK_<Cat>_<Id>.fbx        mesh + skeleton + weights (from blender_gr2_to_fbx.py)
//     Animations/A_<Id>_<State>.fbx   one clip per file
//     Textures/T_<Id>[_Part]_BC.png   from dds_to_png.py
// Output: Materials/M_<Id>.mat (URP Lit), Animations/AC_<Id>.controller, Prefabs/PF_<Cat>_<Id>.prefab
//
// Menu:  MetinAR > Licensed Import > Build Selected Set
// Batch: Unity -batchmode -projectPath <preview> -executeMethod MetinAR.Editor.LicensedImport.LicensedSetBuilder.BuildFromCommandLine -setFolder Assets/MetinAR/Characters/Mobs/<Id> -quit -logFile build.log
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MetinAR.Editor.LicensedImport
{
    public static class LicensedSetBuilder
    {
        static readonly string[] Looping = { "Idle", "Walk", "Run", "Talk", "Hammering" };

        [MenuItem("MetinAR/Licensed Import/Build Selected Set")]
        static void BuildSelected()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!AssetDatabase.IsValidFolder(path)) { Debug.LogError("[LicensedImport] Select the <Id> folder."); return; }
            Build(path);
        }

        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-setFolder");
            if (i < 0 || i + 1 >= args.Length) { Debug.LogError("[LicensedImport] missing -setFolder"); EditorApplication.Exit(2); return; }
            var errors = Build(args[i + 1]);
            EditorApplication.Exit(errors == 0 ? 0 : 1);
        }

        public static int Build(string setFolder)
        {
            int errors = 0;
            string id = Path.GetFileName(setFolder);
            string catFolder = Path.GetFileName(Path.GetDirectoryName(setFolder));
            string cat = catFolder == "NPC" ? "Npc" : catFolder == "Bosses" ? "Boss" : "Mob";
            string modelPath = $"{setFolder}/Models/SK_{cat}_{id}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) { Debug.LogError($"[LicensedImport] missing {modelPath}"); return 1; }

            // 1. model importer: Generic rig (mobs) / Humanoid only when the NPC skeleton maps (decided per set)
            var mi = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            mi.animationType = ModelImporterAnimationType.Generic; // NPC Humanoid mapping is a manual, per-set decision
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.isReadable = false;
            mi.importBlendShapes = false;
            mi.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();

            // 2. clips: copy avatar, one clip per file, loop by state, no root motion for mobs
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { $"{setFolder}/Animations" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var file = Path.GetFileNameWithoutExtension(p);
                if (!file.StartsWith($"A_{id}_")) continue;
                string state = file.Substring($"A_{id}_".Length);
                var ci = (ModelImporter)AssetImporter.GetAtPath(p);
                ci.animationType = ModelImporterAnimationType.Generic;
                ci.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                ci.sourceAvatar = avatar;
                ci.importAnimation = true;
                ci.materialImportMode = ModelImporterMaterialImportMode.None;
                var take = ci.defaultClipAnimations.FirstOrDefault();
                if (take == null) { Debug.LogError($"[LicensedImport] {p}: no take"); errors++; continue; }
                take.name = file;
                take.loopTime = Looping.Contains(state.Split('_')[0]);
                take.lockRootRotation = true; take.lockRootHeightY = true; take.lockRootPositionXZ = cat != "Npc";
                take.keepOriginalOrientation = true; take.keepOriginalPositionY = true; take.keepOriginalPositionXZ = true;
                ci.clipAnimations = new[] { take };
                ci.SaveAndReimport();
                var clip = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip == null) { Debug.LogError($"[LicensedImport] {p}: clip not imported"); errors++; continue; }
                clips[state] = clip;
            }

            // 3. URP Lit material from the _BC texture (alpha clipping only if the texture has alpha)
            Directory.CreateDirectory($"{setFolder}/Materials");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) { Debug.LogError("[LicensedImport] URP Lit shader not found (URP not installed?)"); return errors + 1; }
            var texGuid = AssetDatabase.FindAssets("t:Texture2D", new[] { $"{setFolder}/Textures" })
                .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(t => t.EndsWith("_BC.png") || t.EndsWith("_BC.tga"));
            var mat = new Material(shader) { name = $"M_{id}", enableInstancing = true };
            if (texGuid != null)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(texGuid);
                ti.sRGBTexture = true; ti.alphaIsTransparency = ti.DoesSourceTextureHaveAlpha(); ti.mipmapEnabled = true; ti.SaveAndReimport();
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texGuid);
                mat.SetTexture("_BaseMap", tex);
                if (ti.DoesSourceTextureHaveAlpha())
                {
                    mat.SetFloat("_AlphaClip", 1f); mat.SetFloat("_Cutoff", 0.5f); mat.EnableKeyword("_ALPHATEST_ON");
                }
            }
            else { Debug.LogWarning($"[LicensedImport] {id}: no _BC texture, material left untextured (PLACEHOLDER)"); }
            mat.SetFloat("_Smoothness", 0.1f); mat.SetFloat("_Metallic", 0f); // placeholders: source has no PBR maps
            var matPath = $"{setFolder}/Materials/M_{id}.mat";
            AssetDatabase.CreateAsset(mat, matPath);

            // 4. Animator controller, default state Idle (or first clip)
            Directory.CreateDirectory($"{setFolder}/Animations");
            var ac = AnimatorController.CreateAnimatorControllerAtPath($"{setFolder}/Animations/AC_{id}.controller");
            var sm = ac.layers[0].stateMachine;
            foreach (var kv in clips.OrderBy(k => k.Key == "Idle" ? 0 : 1))
            {
                var st = sm.AddState(kv.Key);
                st.motion = kv.Value;
                if (kv.Key == "Idle") sm.defaultState = st;
            }

            // 5. visual prefab per section 3.7
            var root = new GameObject($"PF_{cat}_{id}");
            var modelGo = new GameObject("Model");
            modelGo.transform.SetParent(root.transform, false);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(modelGo.transform, false);
            var animator = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
            animator.runtimeAnimatorController = ac;
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            var renderers = inst.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers) r.sharedMaterials = Enumerable.Repeat((Material)mat, r.sharedMaterials.Length).ToArray();
            var lod = modelGo.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.02f, renderers) });
            var bounds = renderers.Length > 0 ? renderers.Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }) : new Bounds(Vector3.up * 0.5f, Vector3.one);
            var colliders = new GameObject("Colliders"); colliders.transform.SetParent(root.transform, false);
            var body = new GameObject("Body"); body.transform.SetParent(colliders.transform, false);
            var cap = body.AddComponent<CapsuleCollider>();
            cap.center = bounds.center; cap.height = bounds.size.y; cap.radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            var anchors = new GameObject("Anchors"); anchors.transform.SetParent(root.transform, false);
            var label = new GameObject("Anchor_Label"); label.transform.SetParent(anchors.transform, false);
            label.transform.localPosition = new Vector3(0, bounds.max.y + 0.15f, 0);
            // 6. report numbers (before the temporary hierarchy is destroyed)
            var tris = renderers.OfType<SkinnedMeshRenderer>().Sum(r => r.sharedMesh ? r.sharedMesh.triangles.Length / 3 : 0);
            var bones = renderers.OfType<SkinnedMeshRenderer>().SelectMany(r => r.bones).Distinct().Count();
            Directory.CreateDirectory($"{setFolder}/Prefabs");
            var prefabPath = $"{setFolder}/Prefabs/PF_{cat}_{id}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool ok);
            UnityEngine.Object.DestroyImmediate(root);
            if (!ok) { Debug.LogError($"[LicensedImport] prefab save failed {prefabPath}"); errors++; }

            Debug.Log($"[LicensedImport] {id}: clips={string.Join(",", clips.Keys)} tris={tris} bones={bones} material={matPath} prefab={prefabPath} errors={errors}");
            AssetDatabase.SaveAssets();
            return errors;
        }
    }
}
#endif
