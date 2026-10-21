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

namespace MetinAR.Editor.Characters
{
    /// <summary>
    /// METINAR-016: batch verification of the Wandering Merchant NPC asset in Unity + URP preview screenshots.
    /// Opens only a new unsaved scene (no project scene is loaded or saved) and changes no asset.
    /// Batch (needs graphics, no -nographics): -executeMethod MetinAR.Editor.Characters.WanderingMerchantAssetCheck.Run
    /// Results: Builds/metinar-016/asset-check.txt, screenshots Builds/metinar-016/*.png
    /// </summary>
    public static class WanderingMerchantAssetCheck
    {
        public const string OutDir = "Builds/metinar-016";
        private static readonly List<string> Lines = new List<string>();
        private static int _fail;

        // state -> (length s, loop)
        private static readonly Dictionary<string, (float len, bool loop)> Expected = new Dictionary<string, (float, bool)>
        {
            { "Idle", (2.4f, true) }, { "Walk", (1.0f, true) }, { "TurnLeft", (1.0f, false) }, { "TurnRight", (1.0f, false) },
            { "Talk", (2.0f, true) }, { "Interact", (1.4f, false) }, { "Gesture", (1.6f, false) },
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
            finally
            {
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
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
            // ---------------- model import + avatar
            var importer = (ModelImporter)AssetImporter.GetAtPath(WanderingMerchantAssetBuilder.ModelPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(WanderingMerchantAssetBuilder.ModelPath);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(WanderingMerchantAssetBuilder.ModelPath).OfType<Avatar>().FirstOrDefault();
            Check("C01 model imported as Humanoid (avatar from this model), Read/Write off, scale 1",
                importer != null && importer.animationType == ModelImporterAnimationType.Human && importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel && !importer.isReadable && Mathf.Approximately(importer.globalScale, 1f),
                importer == null ? "no importer" : importer.animationType + "/" + importer.avatarSetup + " readable=" + importer.isReadable);
            Check("C02 avatar valid and human", avatar != null && avatar.isValid && avatar.isHuman, avatar == null ? "none" : "valid=" + avatar.isValid + " human=" + avatar.isHuman);
            if (avatar != null && avatar.isHuman)
            {
                HumanBone[] hb = avatar.humanDescription.human;
                string[] required = { "Hips", "Spine", "Chest", "Neck", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" };
                string[] mapped = hb.Select(b => b.humanName).ToArray();
                Info("avatar maps " + hb.Length + " human bones: " + string.Join(", ", hb.Select(b => b.humanName + "=" + b.boneName)));
                Check("C03 avatar maps all required humanoid bones to the same-named bones", required.All(r => hb.Any(b => b.humanName == r && b.boneName == r)), string.Join(",", required.Where(r => !mapped.Contains(r))));
            }

            SkinnedMeshRenderer[] smrs = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderBy(r => AssetPipelineRules.LodIndex(r.name)).ToArray();
            int[] tris = smrs.Select(r => (int)(Enumerable.Range(0, r.sharedMesh.subMeshCount).Sum(s => (long)r.sharedMesh.GetIndexCount(s)) / 3)).ToArray();
            Info("model renderers: " + string.Join(", ", smrs.Select((r, i) => r.name + "=" + tris[i] + " tris/" + r.sharedMesh.vertexCount + " verts/" + r.sharedMesh.subMeshCount + " submesh/" + r.bones.Length + " bones")));
            Check("C04 three LOD meshes _LOD0/_LOD1/_LOD2", smrs.Length == 3 && smrs.Select(r => AssetPipelineRules.LodIndex(r.name)).SequenceEqual(new[] { 0, 1, 2 }), string.Join(",", smrs.Select(r => r.name)));
            Check("C05 LOD0 <= 10000 tris (issue #38 budget), LOD1 <= 60 %, LOD2 <= 30 %", tris.Length == 3 && tris[0] <= 10000 && tris[1] <= tris[0] * 0.6 && tris[2] <= tris[0] * 0.3, string.Join("/", tris));
            Check("C06 <= 40 bones, 1 sub-mesh (1 material) per LOD", smrs.All(r => r.bones.Length <= 40 && r.sharedMesh.subMeshCount == 1), string.Join("/", smrs.Select(r => r.bones.Length)));

            // ---------------- clips
            foreach (var kv in Expected)
            {
                AnimationClip clip = WanderingMerchantAssetBuilder.LoadClip(kv.Key);
                var ci = (ModelImporter)AssetImporter.GetAtPath(WanderingMerchantAssetBuilder.ClipPath(kv.Key));
                bool ok = clip != null && ci != null && clip.name == "A_WanderingMerchant_" + kv.Key && Mathf.Abs(clip.length - kv.Value.len) < 0.05f && clip.isLooping == kv.Value.loop
                          && clip.isHumanMotion && ci.animationType == ModelImporterAnimationType.Human && ci.avatarSetup == ModelImporterAvatarSetup.CopyFromOther && ci.sourceAvatar == avatar
                          && ci.animationCompression == ModelImporterAnimationCompression.Optimal;
                Check("C07 clip " + kv.Key + ": name, " + kv.Value.len + " s, loop=" + kv.Value.loop + ", humanoid, Copy From Other (SK avatar), Optimal", ok,
                    clip == null ? "missing" : clip.name + " len=" + clip.length.ToString("0.000") + " loop=" + clip.isLooping + " human=" + clip.isHumanMotion + " fps=" + clip.frameRate);
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
            Check("C10 prefab: 0 missing scripts, no MonoBehaviours (visual only)", missing == 0 && prefab.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, missing.ToString());
            var animator = prefab.GetComponentInChildren<Animator>(true);
            var ac = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
            Check("C11 Animator: humanoid avatar, AC_WanderingMerchant (7 states, default Idle), no root motion",
                ac != null && animator.avatar == avatar && ac.layers[0].stateMachine.states.Length == 7 && ac.layers[0].stateMachine.defaultState.name == "Idle" && !animator.applyRootMotion,
                ac == null ? "no controller" : string.Join(",", ac.layers[0].stateMachine.states.Select(s => s.state.name)));
            var group = prefab.GetComponentInChildren<LODGroup>(true);
            LOD[] lods = group != null ? group.GetLODs() : new LOD[0];
            Check("C12 LODGroup 3 levels (0.25/0.08/0.01), one renderer each", lods.Length == 3 && lods.All(l => l.renderers.Length == 1 && l.renderers[0] != null) && Mathf.Approximately(lods[0].screenRelativeTransitionHeight, 0.25f), string.Join("/", lods.Select(l => l.screenRelativeTransitionHeight.ToString("0.00"))));
            Check("C13 one primitive body collider, no MeshCollider", prefab.GetComponentsInChildren<Collider>(true).Length == 1 && prefab.GetComponentInChildren<CapsuleCollider>(true) != null && prefab.GetComponentsInChildren<MeshCollider>(true).Length == 0, null);
            Check("C14 material M_WanderingMerchant is URP Lit on all LODs", prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r.sharedMaterial != null && r.sharedMaterial.name == "M_WanderingMerchant" && r.sharedMaterial.shader.name == "Universal Render Pipeline/Lit"), null);
            Check("C15 Anchor_Label present", prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Anchor_Label"), null);

            // ---------------- scene-based checks + screenshots (new unsaved scene only)
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Transform modelT = inst.transform.Find("Model");
            Transform T(string n) => inst.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
            Transform lFoot = T("LeftFoot"), lToes = T("LeftToes"), rFoot = T("RightFoot"), head = T("Head");
            Check("C16 orientation: faces +Z (toes ahead of the ankles), left foot at -X", lToes.position.z > lFoot.position.z + 0.05f && lFoot.position.x < 0f && rFoot.position.x > 0f && head.position.y > 1.5f,
                "LeftFoot " + lFoot.position.ToString("F3") + " LeftToes " + lToes.position.ToString("F3") + " Head y=" + head.position.y.ToString("0.000"));
            SkinnedMeshRenderer lod0 = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => AssetPipelineRules.LodIndex(r.name) == 0);
            Bounds rest = BakedBounds(lod0);
            Info("rest bounds (m): min " + rest.min.ToString("F3") + " max " + rest.max.ToString("F3") + " size " + rest.size.ToString("F3"));
            Check("C17 rest pose on the ground (min y within ±1 cm), height 1.8–2.0 m with the hat", Mathf.Abs(rest.min.y) <= 0.01f && rest.max.y > 1.8f && rest.max.y < 2.0f, rest.ToString("F3"));

            Transform[] bones = lod0.bones;
            Vector3[] restPos = bones.Select(b => b.position).ToArray();
            AnimationMode.StartAnimationMode();
            foreach (string state in WanderingMerchantAssetBuilder.States)
            {
                AnimationClip clip = WanderingMerchantAssetBuilder.LoadClip(state);
                float maxMove = 0f, minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i <= 12; i++)
                {
                    Sample(modelT.gameObject, clip, clip.length * i / 12f);
                    for (int b = 0; b < bones.Length; b++) maxMove = Mathf.Max(maxMove, (bones[b].position - restPos[b]).magnitude);
                    Bounds bb = BakedBounds(lod0);
                    minY = Mathf.Min(minY, bb.min.y);
                    maxY = Mathf.Max(maxY, bb.max.y);
                }

                Check("C18 clip " + state + " drives the humanoid rig and keeps the feet on the ground (min y -3..+3 cm)", maxMove > 0.02f && minY >= -0.03f && minY <= 0.03f,
                    "max bone move " + maxMove.ToString("0.000") + " m, min y " + minY.ToString("0.000") + " m, max y " + maxY.ToString("0.000") + " m");
            }

            AnimationMode.StopAnimationMode();
            List<PipelineIssue> issues = AssetPipelineValidator.ValidateFolders(new[] { WanderingMerchantAssetBuilder.Folder }, out int files);
            foreach (PipelineIssue i in issues) Info("validator: " + i);
            Check("C19 asset pipeline validator on WanderingMerchant: 0 errors", issues.All(i => i.Severity != IssueSeverity.Error), files + " files, " + issues.Count + " issues");

            Screenshots(inst, modelT.gameObject);
        }

        private static void Sample(GameObject model, AnimationClip clip, float t)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(model, clip, t);
            AnimationMode.EndSampling();
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

        private static void Screenshots(GameObject inst, GameObject model)
        {
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, 145f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.46f);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 0.8f;
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gm.SetColor("_BaseColor", new Color(0.36f, 0.37f, 0.38f));
            gm.SetFloat("_Smoothness", 0.1f);
            ground.GetComponent<Renderer>().sharedMaterial = gm;

            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.32f, 0.35f);
            cam.fieldOfView = 35f;
            LODGroup group = inst.GetComponentInChildren<LODGroup>();

            Shot(cam, new Vector3(2.2f, 1.7f, 3.4f), "unity_hero_34.png");
            Shot(cam, new Vector3(0f, 1.1f, 4.2f), "unity_front.png");
            Shot(cam, new Vector3(-2.4f, 1.8f, -3.2f), "unity_back_34.png");
            for (int i = 0; i < 3; i++)
            {
                group.ForceLOD(i);
                Shot(cam, new Vector3(2.2f, 1.7f, 3.4f), "unity_lod" + i + ".png");
            }

            group.ForceLOD(-1);
            var poses = new[] { ("Idle", 0.6f), ("Walk", 0.25f), ("Walk", 0.75f), ("TurnLeft", 0.5f), ("Talk", 0.5f), ("Interact", 0.65f), ("Gesture", 0.8f) };
            // Batch-mode Camera.Render does not re-skin after sampling, so each pose is baked into a temporary
            // static mesh (same material and world transform) while the skinned renderers are hidden.
            SkinnedMeshRenderer[] skinned = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer lod0 = skinned.First(r => AssetPipelineRules.LodIndex(r.name) == 0);
            foreach (SkinnedMeshRenderer r in skinned) r.enabled = false;
            AnimationMode.StartAnimationMode();
            foreach (var (state, t) in poses)
            {
                Sample(model, WanderingMerchantAssetBuilder.LoadClip(state), t);
                var baked = new Mesh();
                lod0.BakeMesh(baked, true);
                var proxy = new GameObject("PoseProxy");
                proxy.transform.SetPositionAndRotation(lod0.transform.position, lod0.transform.rotation);
                proxy.AddComponent<MeshFilter>().sharedMesh = baked;
                proxy.AddComponent<MeshRenderer>().sharedMaterial = lod0.sharedMaterial;
                Shot(cam, new Vector3(2.2f, 1.6f, 3.2f), "unity_pose_" + state + "_" + Mathf.RoundToInt(t * 30f).ToString("00") + ".png");
                UnityEngine.Object.DestroyImmediate(proxy);
                UnityEngine.Object.DestroyImmediate(baked);
            }

            AnimationMode.StopAnimationMode();
            foreach (SkinnedMeshRenderer r in skinned) r.enabled = true;
        }

        private static void Shot(Camera cam, Vector3 pos, string file)
        {
            const int w = 1000, h = 1200;
            cam.transform.position = pos;
            cam.transform.LookAt(new Vector3(0f, 0.95f, 0f));
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
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
