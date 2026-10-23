using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MetinAR.Editor.AssetPipeline;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetinAR.Editor.WanderingMerchant
{
    /// <summary>
    /// METINAR-016: batch verification of the Wandering Merchant NPC asset in Unity + URP preview screenshots.
    /// Opens only a new unsaved scene (no project scene is loaded or saved) and changes no asset.
    /// Batch (needs graphics, no -nographics): -executeMethod MetinAR.Editor.WanderingMerchant.WanderingMerchantAssetCheck.Run
    /// Results: Builds/metinar-016/asset-check.txt, screenshots Builds/metinar-016/*.png
    /// </summary>
    public static class WanderingMerchantAssetCheck
    {
        public const string OutDir = "Builds/metinar-016";
        private static readonly List<string> Lines = new List<string>();
        private static int _fail;

        // state → (length s, loop)
        private static readonly Dictionary<string, (float len, bool loop)> Expected = new Dictionary<string, (float, bool)>
        {
            { "Idle", (3.0f, true) }, { "Walk", (1.2f, true) }, { "TurnLeft", (1.0f, false) },
            { "TurnRight", (1.0f, false) }, { "Talk", (2.0f, true) }, { "Interact", (2.0f, false) },
        };

        public static void Run()
        {
            Lines.Clear();
            _fail = 0;
            Directory.CreateDirectory(OutDir);
            try
            {
                Execute();
            }
            catch (Exception e)
            {
                Fail("X00 no exception", e.ToString());
            }

            var sb = new StringBuilder();
            sb.AppendLine("[METINAR-016] Wandering Merchant asset check: checks=" + Lines.Count(l => l.StartsWith("PASS") || l.StartsWith("FAIL")) + " failures=" + _fail);
            foreach (string l in Lines) sb.AppendLine(l);
            File.WriteAllText(OutDir + "/asset-check.txt", sb.ToString());
            Debug.Log(sb.ToString());
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        private static void Execute()
        {
            const string B = nameof(WanderingMerchantAssetBuilder);
            Info("builder " + B + ", Unity " + Application.unityVersion + ", pipeline " + (GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.GetType().Name : "built-in"));

            // ---------------- model import
            var importer = (ModelImporter)AssetImporter.GetAtPath(WanderingMerchantAssetBuilder.ModelPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(WanderingMerchantAssetBuilder.ModelPath);
            Avatar avatar = WanderingMerchantAssetBuilder.LoadAvatar();
            Check("C01 model imported as Humanoid (Create From This Model), Read/Write off, scale 1",
                importer != null && importer.animationType == ModelImporterAnimationType.Human && importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel && !importer.isReadable && Mathf.Approximately(importer.globalScale, 1f),
                importer == null ? "no importer" : importer.animationType + "/" + importer.avatarSetup + " readable=" + importer.isReadable);
            Check("C02 humanoid avatar valid", avatar != null && avatar.isValid && avatar.isHuman, avatar == null ? "no avatar" : "valid=" + avatar.isValid + " human=" + avatar.isHuman);
            HumanBone[] human = avatar != null ? avatar.humanDescription.human : new HumanBone[0];
            Info("avatar mapping (" + human.Length + "): " + string.Join(", ", human.Select(h => h.humanName + "=" + h.boneName)));
            string[] requiredHuman = { "Hips", "Spine", "Chest", "Neck", "Jaw", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" };
            var mapped = new HashSet<string>(human.Select(h => h.humanName));
            string[] missingHuman = requiredHuman.Where(h => !mapped.Contains(HumanTrait.BoneName[(int)Enum.Parse(typeof(HumanBodyBones), h)])).ToArray();
            Check("C03 avatar maps every required humanoid bone", missingHuman.Length == 0, missingHuman.Length == 0 ? human.Length + " bones mapped" : "missing " + string.Join(",", missingHuman));

            SkinnedMeshRenderer[] smrs = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderBy(r => AssetPipelineRules.LodIndex(r.name)).ToArray();
            int[] tris = smrs.Select(r => (int)(Enumerable.Range(0, r.sharedMesh.subMeshCount).Sum(s => (long)r.sharedMesh.GetIndexCount(s)) / 3)).ToArray();
            Info("model renderers: " + string.Join(", ", smrs.Select((r, i) => r.name + "=" + tris[i] + " tris/" + r.sharedMesh.vertexCount + " verts/" + r.sharedMesh.subMeshCount + " submesh/" + r.bones.Length + " bones")));
            Check("C04 three LOD meshes _LOD0/_LOD1/_LOD2", smrs.Length == 3 && smrs.Select(r => AssetPipelineRules.LodIndex(r.name)).SequenceEqual(new[] { 0, 1, 2 }), string.Join(",", smrs.Select(r => r.name)));
            Check("C05 LOD0 <= 10000 tris, LOD1 <= 60 %, LOD2 <= 30 % of LOD0", tris.Length == 3 && tris[0] <= 10000 && tris[1] <= tris[0] * 0.6 && tris[2] <= tris[0] * 0.3, string.Join("/", tris));
            Check("C06 <= 40 bones, 1 sub-mesh (1 material) per LOD", smrs.All(r => r.bones.Length <= 40 && r.sharedMesh.subMeshCount == 1), string.Join("/", smrs.Select(r => r.bones.Length)));

            // ---------------- clips
            foreach (var kv in Expected)
            {
                string cp = WanderingMerchantAssetBuilder.ClipPath(kv.Key);
                var dimp = (ModelImporter)AssetImporter.GetAtPath(cp);
                Info("clip file " + kv.Key + ": sub-assets [" + string.Join(", ", AssetDatabase.LoadAllAssetsAtPath(cp).Select(o => o.GetType().Name + ":" + o.name)) + "] type=" + dimp.animationType + " avatarSetup=" + dimp.avatarSetup
                     + " source=" + (dimp.sourceAvatar != null ? dimp.sourceAvatar.name : "-") + " clips=" + dimp.clipAnimations.Length + " takes=" + (dimp.importedTakeInfos != null ? dimp.importedTakeInfos.Length : -1)
                     + " (import messages: see the clip .meta animationImportErrors/Warnings)");
            }

            foreach (var kv in Expected)
            {
                AnimationClip clip = WanderingMerchantAssetBuilder.LoadClip(kv.Key);
                var ci = (ModelImporter)AssetImporter.GetAtPath(WanderingMerchantAssetBuilder.ClipPath(kv.Key));
                bool ok = clip != null && clip.name == "A_WanderingMerchant_" + kv.Key && Mathf.Abs(clip.length - kv.Value.len) < 0.05f && clip.isLooping == kv.Value.loop
                          && clip.isHumanMotion && ci.animationType == ModelImporterAnimationType.Human && ci.avatarSetup == ModelImporterAvatarSetup.CopyFromOther && ci.sourceAvatar == avatar
                          && ci.animationCompression == ModelImporterAnimationCompression.Optimal;
                Check("C07 clip " + kv.Key + ": name, " + kv.Value.len + " s, loop=" + kv.Value.loop + ", humanoid, Copy From Other Avatar, Optimal", ok,
                    clip == null ? "missing" : clip.name + " len=" + clip.length.ToString("0.000") + " loop=" + clip.isLooping + " human=" + clip.isHumanMotion + " avatarSetup=" + ci.avatarSetup);
            }

            // ---------------- textures
            foreach (string role in new[] { "BC", "N", "MS", "E" })
            {
                string p = WanderingMerchantAssetBuilder.Folder + "/Textures/T_WanderingMerchant_" + role + ".png";
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                ti.GetSourceTextureWidthAndHeight(out int w, out int h);
                TextureImporterPlatformSettings a = ti.GetPlatformTextureSettings("Android");
                Info("texture " + role + ": " + w + "x" + h + " type=" + ti.textureType + " sRGB=" + ti.sRGBTexture + " max=" + ti.maxTextureSize + " Android=" + a.format);
                Check("C08 texture " + role + " 1024x1024 with ASTC override", w == 1024 && h == 1024 && a.overridden && a.format.ToString().StartsWith("ASTC"), w + "x" + h + " " + a.format);
            }

            // ---------------- prefab
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WanderingMerchantAssetBuilder.PrefabPath);
            Check("C09 prefab PF_Npc_WanderingMerchant loads", prefab != null, null);
            int missing = prefab.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            Check("C10 prefab: 0 missing scripts, root at identity", missing == 0 && prefab.transform.localScale == Vector3.one && prefab.transform.localRotation == Quaternion.identity, missing.ToString());
            var animator = prefab.GetComponentInChildren<Animator>(true);
            var ac = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
            Check("C11 Animator: humanoid avatar, AC_WanderingMerchant (6 states, default Idle), no root motion",
                ac != null && animator.avatar == avatar && ac.layers[0].stateMachine.states.Length == 6 && ac.layers[0].stateMachine.defaultState.name == "Idle" && !animator.applyRootMotion,
                ac == null ? "no controller" : string.Join(",", ac.layers[0].stateMachine.states.Select(s => s.state.name)));
            var group = prefab.GetComponentInChildren<LODGroup>(true);
            LOD[] lods = group != null ? group.GetLODs() : new LOD[0];
            Check("C12 LODGroup 3 levels, one renderer each", lods.Length == 3 && lods.All(l => l.renderers.Length == 1 && l.renderers[0] != null), string.Join("/", lods.Select(l => l.screenRelativeTransitionHeight.ToString("0.000"))));
            Check("C13 one primitive body collider, no MeshCollider", prefab.GetComponentsInChildren<Collider>(true).Length == 1 && prefab.GetComponentInChildren<CapsuleCollider>(true) != null && prefab.GetComponentsInChildren<MeshCollider>(true).Length == 0, null);
            Check("C14 material M_WanderingMerchant is URP Lit on all LODs", prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r.sharedMaterial != null && r.sharedMaterial.name == "M_WanderingMerchant" && r.sharedMaterial.shader.name == "Universal Render Pipeline/Lit"), null);
            string[] anchors = { "Anchor_Label", "Anchor_Interact", "Socket_HandR" };
            Check("C15 Anchor_Label, Anchor_Interact, Socket_HandR present", anchors.All(n => prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == n)), null);

            // ---------------- scene-based checks + screenshots (new unsaved scene only)
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Animator anim = inst.GetComponentInChildren<Animator>();
            Transform toes = anim.GetBoneTransform(HumanBodyBones.LeftToes);
            Transform foot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
            Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            Check("C16 orientation: faces +Z (toes in front of the ankle)", toes != null && foot != null && toes.position.z - foot.position.z > 0.05f, toes == null ? "no toes bone" : "toes z-ankle z=" + (toes.position.z - foot.position.z).ToString("0.000"));
            SkinnedMeshRenderer lod0 = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => AssetPipelineRules.LodIndex(r.name) == 0);
            Sample(anim, WanderingMerchantAssetBuilder.LoadClip("Idle"), 0f);
            Bounds rest = BakedBounds(lod0);
            Info("Idle frame 0 bounds (m): min " + rest.min.ToString("F3") + " max " + rest.max.ToString("F3") + " size " + rest.size.ToString("F3"));
            Check("C17 standing on the ground (min y within ±2 cm), height 1.85–2.0 m (with hat)", Mathf.Abs(rest.min.y) <= 0.02f && rest.max.y > 1.85f && rest.max.y < 2.0f, rest.ToString("F3"));

            Transform[] bones = lod0.bones;
            foreach (string state in WanderingMerchantAssetBuilder.States)
            {
                AnimationClip clip = WanderingMerchantAssetBuilder.LoadClip(state);
                Sample(anim, clip, 0f);
                Vector3[] start = bones.Select(b => b.position).ToArray();
                float maxMove = 0f, minY = float.MaxValue, headMin = float.MaxValue;
                for (int i = 0; i <= 12; i++)
                {
                    Sample(anim, clip, clip.length * i / 12f);
                    for (int b = 0; b < bones.Length; b++) maxMove = Mathf.Max(maxMove, (bones[b].position - start[b]).magnitude);
                    minY = Mathf.Min(minY, BakedBounds(lod0).min.y);
                    headMin = Mathf.Min(headMin, head.position.y);
                }

                Check("C18 clip " + state + " drives the humanoid rig (bones move), stays on the ground (min y >= -3 cm), stays upright (head > 1.4 m)",
                    maxMove > 0.01f && minY >= -0.03f && headMin > 1.4f, "max bone move " + maxMove.ToString("0.000") + " m, min y " + minY.ToString("0.000") + " m, head min y " + headMin.ToString("0.00") + ", hips " + hips.position.ToString("F2"));
            }

            Sample(anim, WanderingMerchantAssetBuilder.LoadClip("Idle"), 0f);
            List<PipelineIssue> issues = AssetPipelineValidator.ValidateFolders(new[] { WanderingMerchantAssetBuilder.Folder }, out int files);
            foreach (PipelineIssue i in issues) Info("validator: " + i);
            Check("C19 asset pipeline validator (current NPC rules) on WanderingMerchant: 0 errors", issues.All(i => i.Severity != IssueSeverity.Error), files + " files, " + issues.Count + " issues (" + issues.Count(i => i.Severity == IssueSeverity.Error) + " errors)");

            Screenshots(inst, anim);
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        }

        // Edit-mode sampling the same way the Animation window does (AnimationMode), so humanoid
        // muscle curves are retargeted through the Animator's avatar onto the bone transforms.
        private static void Sample(Animator animator, AnimationClip clip, float time)
        {
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            try
            {
                AnimationMode.SampleAnimationClip(animator.gameObject, clip, time);
            }
            finally
            {
                AnimationMode.EndSampling();
            }
        }

        private static Bounds BakedBounds(SkinnedMeshRenderer smr)
        {
            var mesh = new Mesh();
            smr.BakeMesh(mesh, true);
            Vector3[] v = mesh.vertices;
            Matrix4x4 m = smr.transform.localToWorldMatrix;
            var b = new Bounds(m.MultiplyPoint3x4(v[0]), Vector3.zero);
            foreach (Vector3 p in v) b.Encapsulate(m.MultiplyPoint3x4(p));
            UnityEngine.Object.DestroyImmediate(mesh);
            return b;
        }

        private static void Screenshots(GameObject inst, Animator anim)
        {
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.50f);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 0.8f;
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gm.SetColor("_BaseColor", new Color(0.30f, 0.31f, 0.32f));
            gm.SetFloat("_Smoothness", 0.1f);
            ground.GetComponent<Renderer>().sharedMaterial = gm;

            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.32f, 0.34f, 0.38f);
            cam.fieldOfView = 35f;
            LODGroup group = inst.GetComponentInChildren<LODGroup>();
            SkinnedMeshRenderer[] skinned = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderBy(r => AssetPipelineRules.LodIndex(r.name)).ToArray();
            foreach (SkinnedMeshRenderer r in skinned) r.enabled = false;

            // Batch-mode Camera.Render does not re-skin after a manual evaluation, so each pose is baked into a temporary
            // static mesh (same material, same world transform) and the skinned renderers stay hidden.
            void Pose(string state, float t, int lod, Vector3 camPos, string file)
            {
                Sample(anim, WanderingMerchantAssetBuilder.LoadClip(state), t);
                SkinnedMeshRenderer src = skinned[lod];
                var baked = new Mesh();
                src.BakeMesh(baked, true);
                var proxy = new GameObject("PoseProxy");
                proxy.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
                proxy.AddComponent<MeshFilter>().sharedMesh = baked;
                proxy.AddComponent<MeshRenderer>().sharedMaterial = src.sharedMaterial;
                Shot(cam, camPos, file);
                UnityEngine.Object.DestroyImmediate(proxy);
                UnityEngine.Object.DestroyImmediate(baked);
            }

            Pose("Idle", 0.75f, 0, new Vector3(2.2f, 1.7f, 3.8f), "unity_hero_34.png");
            Pose("Idle", 0.75f, 0, new Vector3(0f, 1.3f, 4.6f), "unity_front.png");
            Pose("Idle", 0.75f, 0, new Vector3(-2.0f, 1.6f, -3.8f), "unity_back_34.png");
            for (int i = 0; i < 3; i++) Pose("Idle", 0.75f, i, new Vector3(2.2f, 1.7f, 3.8f), "unity_lod" + i + ".png");
            var poses = new[] { ("Walk", 0f), ("Walk", 0.3f), ("TurnLeft", 0.5f), ("TurnRight", 1.0f), ("Talk", 0.27f), ("Talk", 0.77f), ("Interact", 0.97f) };
            foreach (var (state, t) in poses)
            {
                Pose(state, t, 0, new Vector3(2.6f, 1.6f, 3.6f), "unity_pose_" + state + "_" + Mathf.RoundToInt(t * 30f).ToString("00") + ".png");
            }

            foreach (SkinnedMeshRenderer r in skinned) r.enabled = true;
            group.ForceLOD(-1);
        }

        private static void Shot(Camera cam, Vector3 pos, string file)
        {
            cam.transform.position = pos;
            cam.transform.LookAt(new Vector3(0f, 0.98f, 0f));
            var rt = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 960, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0);
            tex.Apply();
            File.WriteAllBytes(OutDir + "/" + file, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            Info("screenshot " + file);
        }

        private static void Info(string s) => Lines.Add("INFO " + s);

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) Lines.Add("PASS " + name + (detail != null ? " — " + detail : string.Empty));
            else Fail(name, detail);
        }

        private static void Fail(string name, string detail)
        {
            _fail++;
            Lines.Add("FAIL " + name + (detail != null ? " — " + detail : string.Empty));
        }
    }
}
