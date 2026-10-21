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
    /// METINAR-013: batch verification of the Venomous Spider asset in Unity + preview screenshots.
    /// Opens only a new unsaved scene (no project scene is loaded or saved) and changes no asset.
    /// Batch (needs graphics, no -nographics): -executeMethod MetinAR.Editor.Characters.VenomousSpiderAssetCheck.Run
    /// Results: Builds/metinar-013/asset-check.txt, screenshots Builds/metinar-013/*.png
    /// </summary>
    public static class VenomousSpiderAssetCheck
    {
        public const string OutDir = "Builds/metinar-013";
        private static readonly List<string> Lines = new List<string>();
        private static int _fail;

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
            sb.AppendLine("[METINAR-013] Venomous Spider asset check: checks=" + Lines.Count(l => l.StartsWith("PASS") || l.StartsWith("FAIL")) + " failures=" + _fail);
            foreach (string l in Lines) sb.AppendLine(l);
            File.WriteAllText(OutDir + "/asset-check.txt", sb.ToString());
            Debug.Log(sb.ToString());
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        private static void Execute()
        {
            // ---------------- model import
            var importer = (ModelImporter)AssetImporter.GetAtPath(VenomousSpiderAssetBuilder.ModelPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(VenomousSpiderAssetBuilder.ModelPath);
            Check("C01 model imported as Generic rig, Read/Write off, scale 1", importer != null && importer.animationType == ModelImporterAnimationType.Generic && !importer.isReadable && Mathf.Approximately(importer.globalScale, 1f), importer == null ? "no importer" : importer.animationType + " readable=" + importer.isReadable);
            SkinnedMeshRenderer[] smrs = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderBy(r => AssetPipelineRules.LodIndex(r.name)).ToArray();
            int[] tris = smrs.Select(r => (int)(Enumerable.Range(0, r.sharedMesh.subMeshCount).Sum(s => (long)r.sharedMesh.GetIndexCount(s)) / 3)).ToArray();
            Info("model renderers: " + string.Join(", ", smrs.Select((r, i) => r.name + "=" + tris[i] + " tris/" + r.sharedMesh.vertexCount + " verts/" + r.sharedMesh.subMeshCount + " submesh/" + r.bones.Length + " bones")));
            Check("C02 three LOD meshes _LOD0/_LOD1/_LOD2", smrs.Length == 3 && smrs.Select(r => AssetPipelineRules.LodIndex(r.name)).SequenceEqual(new[] { 0, 1, 2 }), string.Join(",", smrs.Select(r => r.name)));
            Check("C03 LOD0 <= 8000 tris, LOD1 ~50 %, LOD2 ~25 %", tris.Length == 3 && tris[0] <= 8000 && tris[1] <= tris[0] * 0.6 && tris[2] <= tris[0] * 0.3, string.Join("/", tris));
            Check("C04 <= 40 bones, 1 sub-mesh (1 material) per LOD", smrs.All(r => r.bones.Length <= 40 && r.sharedMesh.subMeshCount == 1), string.Join("/", smrs.Select(r => r.bones.Length)));
            Check("C05 LODGroup auto-created on the model with 3 levels", model.GetComponent<LODGroup>() != null && model.GetComponent<LODGroup>().lodCount == 3, null);

            // ---------------- clips
            var expected = new Dictionary<string, (float len, bool loop)> { { "Idle", (2.4f, true) }, { "Walk", (1.0f, true) }, { "Attack", (1.2f, false) }, { "Hit", (0.4f, false) }, { "Death", (2.0f, false) } };
            foreach (var kv in expected)
            {
                AnimationClip clip = VenomousSpiderAssetBuilder.LoadClip(kv.Key);
                var ci = (ModelImporter)AssetImporter.GetAtPath(VenomousSpiderAssetBuilder.ClipPath(kv.Key));
                bool ok = clip != null && clip.name == "A_VenomousSpider_" + kv.Key && Mathf.Abs(clip.length - kv.Value.len) < 0.05f && clip.isLooping == kv.Value.loop && ci.animationCompression == ModelImporterAnimationCompression.Optimal && ci.animationType == ModelImporterAnimationType.Generic;
                Check("C06 clip " + kv.Key + ": name, length " + kv.Value.len + " s, loop=" + kv.Value.loop + ", Optimal, Generic", ok, clip == null ? "missing" : clip.name + " len=" + clip.length.ToString("0.000") + " loop=" + clip.isLooping + " frameRate=" + clip.frameRate);
            }

            // ---------------- textures
            foreach (string role in new[] { "BC", "N", "MS", "E" })
            {
                string p = VenomousSpiderAssetBuilder.Folder + "/Textures/T_VenomousSpider_" + role + ".png";
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                ti.GetSourceTextureWidthAndHeight(out int w, out int h);
                TextureImporterPlatformSettings a = ti.GetPlatformTextureSettings("Android");
                Info("texture " + role + ": " + w + "x" + h + " type=" + ti.textureType + " sRGB=" + ti.sRGBTexture + " max=" + ti.maxTextureSize + " Android=" + a.format);
                Check("C07 texture " + role + " 1024x1024 with ASTC override", w == 1024 && h == 1024 && a.overridden && a.format.ToString().StartsWith("ASTC"), w + "x" + h + " " + a.format);
            }

            // ---------------- prefab
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VenomousSpiderAssetBuilder.PrefabPath);
            Check("C08 prefab PF_Mob_VenomousSpider loads", prefab != null, null);
            int missing = prefab.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            Check("C09 prefab: 0 missing scripts", missing == 0, missing.ToString());
            var animator = prefab.GetComponentInChildren<Animator>(true);
            var ac = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
            Check("C10 Animator with AC_VenomousSpider (5 states, default Idle, no root motion)", ac != null && ac.layers[0].stateMachine.states.Length == 5 && ac.layers[0].stateMachine.defaultState.name == "Idle" && !animator.applyRootMotion, ac == null ? "no controller" : string.Join(",", ac.layers[0].stateMachine.states.Select(s => s.state.name)));
            var group = prefab.GetComponentInChildren<LODGroup>(true);
            LOD[] lods = group != null ? group.GetLODs() : new LOD[0];
            Check("C11 LODGroup 3 levels (0.25/0.08/0.01), one renderer each", lods.Length == 3 && lods.All(l => l.renderers.Length == 1 && l.renderers[0] != null) && Mathf.Approximately(lods[0].screenRelativeTransitionHeight, 0.25f), string.Join("/", lods.Select(l => l.screenRelativeTransitionHeight.ToString("0.00"))));
            Check("C12 one primitive body collider, no MeshCollider", prefab.GetComponentsInChildren<Collider>(true).Length == 1 && prefab.GetComponentInChildren<CapsuleCollider>(true) != null && prefab.GetComponentsInChildren<MeshCollider>(true).Length == 0, null);
            Check("C13 material M_VenomousSpider is URP Lit on all LODs", prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r.sharedMaterial != null && r.sharedMaterial.name == "M_VenomousSpider" && r.sharedMaterial.shader.name == "Universal Render Pipeline/Lit"), null);
            Check("C14 Anchor_Label present", prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Anchor_Label"), null);

            // ---------------- scene-based checks + screenshots (new unsaved scene only)
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Transform modelT = inst.transform.Find("Model");
            Transform head = inst.GetComponentsInChildren<Transform>(true).First(t => t.name == "B_Head");
            Transform abdomen = inst.GetComponentsInChildren<Transform>(true).First(t => t.name == "B_Abdomen");
            Check("C15 orientation: head at +Z, abdomen at -Z (faces +Z)", head.position.z > 0.15f && abdomen.position.z < 0.05f, "head z=" + head.position.z.ToString("0.000") + " abdomen z=" + abdomen.position.z.ToString("0.000"));
            SkinnedMeshRenderer lod0 = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => AssetPipelineRules.LodIndex(r.name) == 0);
            Bounds rest = BakedBounds(lod0);
            Info("rest bounds (m): min " + rest.min.ToString("F3") + " max " + rest.max.ToString("F3") + " size " + rest.size.ToString("F3"));
            Check("C16 rest pose on the ground (min y within ±1 cm), span ≈ 1.3 m, top ≈ 0.6 m", Mathf.Abs(rest.min.y) <= 0.01f && Mathf.Abs(rest.size.x - 1.3f) < 0.05f && rest.max.y > 0.55f && rest.max.y < 0.7f, rest.ToString("F3"));

            Transform[] bones = lod0.bones;
            Vector3[] restPos = bones.Select(b => b.position).ToArray();
            foreach (string state in VenomousSpiderAssetBuilder.States)
            {
                AnimationClip clip = VenomousSpiderAssetBuilder.LoadClip(state);
                EditorCurveBinding[] binds = AnimationUtility.GetCurveBindings(clip);
                int bound = binds.Count(bd => modelT.Find(bd.path) != null || bd.path.Length == 0);
                Info("clip " + state + ": " + binds.Length + " curves, " + bound + " bind to the model, root curves=" + binds.Count(bd => bd.path.Length == 0) + ", e.g. " + string.Join(" | ", binds.Select(bd => bd.path).Distinct().Take(3)));
                float maxMove = 0f, minY = float.MaxValue;
                for (int i = 0; i <= 10; i++)
                {
                    clip.SampleAnimation(modelT.gameObject, clip.length * i / 10f);
                    for (int b = 0; b < bones.Length; b++) maxMove = Mathf.Max(maxMove, (bones[b].position - restPos[b]).magnitude);
                    minY = Mathf.Min(minY, BakedBounds(lod0).min.y);
                }

                Check("C17 clip " + state + " drives the rig (bones move) and stays above ground (min y >= -2 cm)", maxMove > 0.01f && minY >= -0.02f, "max bone move " + maxMove.ToString("0.000") + " m, min y " + minY.ToString("0.000") + " m");
            }

            ResetPose(modelT.gameObject);
            List<PipelineIssue> issues = AssetPipelineValidator.ValidateFolders(new[] { VenomousSpiderAssetBuilder.Folder }, out int files);
            foreach (PipelineIssue i in issues) Info("validator: " + i);
            Check("C18 asset pipeline validator on VenomousSpider: 0 errors", issues.All(i => i.Severity != IssueSeverity.Error), files + " files, " + issues.Count + " issues");

            Screenshots(inst, modelT.gameObject);
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

        private static void ResetPose(GameObject model)
        {
            VenomousSpiderAssetBuilder.LoadClip("Idle").SampleAnimation(model, 0f);
        }

        private static void Screenshots(GameObject inst, GameObject model)
        {
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.46f);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 0.6f;
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gm.SetColor("_BaseColor", new Color(0.36f, 0.37f, 0.38f));
            gm.SetFloat("_Smoothness", 0.1f);
            ground.GetComponent<Renderer>().sharedMaterial = gm;

            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.32f, 0.35f);
            cam.fieldOfView = 35f;
            LODGroup group = inst.GetComponentInChildren<LODGroup>();

            Shot(cam, new Vector3(1.9f, 1.3f, 2.3f), "unity_hero_34.png");
            Shot(cam, new Vector3(0f, 0.9f, 3.2f), "unity_front.png");
            for (int i = 0; i < 3; i++)
            {
                group.ForceLOD(i);
                Shot(cam, new Vector3(1.9f, 1.3f, 2.3f), "unity_lod" + i + ".png");
            }

            group.ForceLOD(-1);
            var poses = new[] { ("Idle", 1.2f), ("Walk", 0.25f), ("Attack", 0.4f), ("Attack", 17f / 30f), ("Hit", 0.1f), ("Death", 2.0f) };
            // Batch-mode Camera.Render does not re-skin after SampleAnimation, so each pose is baked into a
            // temporary static mesh (same material, same world transform) and the skinned renderers are hidden.
            SkinnedMeshRenderer[] skinned = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer lod0 = skinned.First(r => AssetPipelineRules.LodIndex(r.name) == 0);
            foreach (SkinnedMeshRenderer r in skinned) r.enabled = false;
            foreach (var (state, t) in poses)
            {
                VenomousSpiderAssetBuilder.LoadClip(state).SampleAnimation(model, t);
                var baked = new Mesh();
                lod0.BakeMesh(baked, true);
                var proxy = new GameObject("PoseProxy");
                proxy.transform.SetPositionAndRotation(lod0.transform.position, lod0.transform.rotation);
                proxy.AddComponent<MeshFilter>().sharedMesh = baked;
                proxy.AddComponent<MeshRenderer>().sharedMaterial = lod0.sharedMaterial;
                Shot(cam, new Vector3(1.9f, 1.2f, 2.1f), "unity_pose_" + state + "_" + Mathf.RoundToInt(t * 30f).ToString("00") + ".png");
                UnityEngine.Object.DestroyImmediate(proxy);
                UnityEngine.Object.DestroyImmediate(baked);
            }

            foreach (SkinnedMeshRenderer r in skinned) r.enabled = true;

            ResetPose(model);
        }

        private static void Shot(Camera cam, Vector3 pos, string file)
        {
            cam.transform.position = pos;
            cam.transform.LookAt(new Vector3(0f, 0.25f, 0f));
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
