using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MetinAR.Editor.AssetPipeline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetinAR.Editor.FppArt
{
    /// <summary>
    /// METINAR-FPP-ART-001: read-only check of the FPP hands + sword assets in Unity.
    /// - prefabs load, no missing scripts / materials, URP shaders, shadows off, sockets present;
    /// - every clip sampled at 100 Hz: B_Socket_Sword vs the PR #32 RightGrip pose and B_Socket_GripL vs the
    ///   PR #32 left-hand pose of contract V3 "v3-a31-retest" (reference values copied below, see docs/METINAR-FPP-ART-001.md §2);
    /// - hand/sword intersection on the skinned mesh (BakeMesh) against the sword proxies;
    /// - visibility of grip/guard/blade/left hand in the estimated A31 portrait + landscape view;
    /// - preview screenshots (FPP portrait/landscape, outside, grip close-up) into Builds/metinar-fpp-art/.
    /// Creates an unsaved temporary scene only; never modifies assets or project scenes.
    /// Batch: -executeMethod MetinAR.Editor.FppArt.FppArtAssetCheck.RunBatch
    /// </summary>
    public static class FppArtAssetCheck
    {
        public const string OutDir = "Builds/metinar-fpp-art";
        public const string ReportPath = OutDir + "/fpp-art-check.txt";
        public const float PortraitVerticalFov = 69.4f; // estimate for the Galaxy A31 rear camera, portrait (see doc §6)
        public const float PortraitAspect = 540f / 1200f;
        public const float MaxPositionError = 0.002f;
        public const float MaxRotationErrorDeg = 1.0f;

        // ---- Contract V3 "v3-a31-retest" (#31 issuecomment-6314043034; PR #32 8d0acee FppViewFraming =
        //      FppPoseContract.RetestV3) / FppCombatTimeline technical values, copied for verification only
        public static readonly Vector3 RestPosition = new Vector3(0.09f, -0.17f, 0.46f);
        public static readonly Vector3 RestEuler = new Vector3(15f, -30f, -22f);
        public static readonly Vector3 LeftRest = new Vector3(-0.08f, -0.23f, 0.42f);
        public static readonly Vector3 LeftRestEuler = new Vector3(0f, 0f, 12f);
        public const float SwordScale = 0.75f;
        /// <summary>Unscaled sword-model points (sockets of SM_Weapon_FppSword / PF_Weapon_FppSword).</summary>
        public static readonly Vector3 ModelLeftGripOffset = new Vector3(0f, -0.12f, 0f);
        public static readonly Vector3 ModelBladeBase = new Vector3(0f, 0.13f, 0f), ModelBladeTip = new Vector3(0f, 0.94f, 0f);
        public static readonly Vector3 ModelAuraBurst = new Vector3(0f, 0.38f, 0f);
        /// <summary>Contract points in the RightGrip frame = model x SwordScale (hit blade, left hand on the hilt).</summary>
        public static readonly Vector3 LeftGripOffset = ModelLeftGripOffset * SwordScale;
        public static readonly Vector3 BladeTip = ModelBladeTip * SwordScale;
        public static readonly Vector3 WindupPos = new Vector3(0.25f, -0.08f, 0.48f), WindupEuler = new Vector3(0f, -10f, -65f);
        public static readonly Vector3 EndPos = new Vector3(-0.15f, -0.22f, 0.50f), EndEuler = new Vector3(70f, 30f, 60f);
        public static readonly Vector3 AuraPos = new Vector3(0.10f, -0.14f, 0.50f), AuraEuler = new Vector3(10f, -30f, -18f);
        public const float AttackSeconds = 0.72f, HitStart = 0.18f, HitEnd = 0.42f, AuraCastSeconds = 1.15f, IdleSeconds = 2.0f;

        private static float Ease(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        public static void SwordPose(string action, float t, out Vector3 p, out Quaternion r)
        {
            Quaternion rest = Quaternion.Euler(RestEuler);
            p = RestPosition;
            r = rest;
            if (action == "Attack")
            {
                Quaternion w = Quaternion.Euler(WindupEuler), e = Quaternion.Euler(EndEuler);
                if (t < HitStart) { float k = Ease(t / HitStart); p = Vector3.Lerp(RestPosition, WindupPos, k); r = Quaternion.Slerp(rest, w, k); }
                else if (t <= HitEnd) { float k = Ease((t - HitStart) / (HitEnd - HitStart)); p = Vector3.Lerp(WindupPos, EndPos, k); r = Quaternion.Slerp(w, e, k); }
                else { float k = Ease((t - HitEnd) / (AttackSeconds - HitEnd)); p = Vector3.Lerp(EndPos, RestPosition, k); r = Quaternion.Slerp(e, rest, k); }
            }
            else if (action == "AuraActivate")
            {
                float b = t < 0.9f ? Ease(t / 0.25f) : 1f - Ease((t - 0.9f) / 0.25f);
                p = Vector3.Lerp(RestPosition, AuraPos, b);
                r = Quaternion.Slerp(rest, Quaternion.Euler(AuraEuler), b);
            }
        }

        public static float LeftGripBlend(string action, float t)
        {
            if (action != "AuraActivate") return 0f;
            return t < 0.9f ? Ease(t / 0.35f) : 1f - Ease((t - 0.9f) / 0.25f);
        }

        public static void LeftHandPose(string action, float t, out Vector3 p, out Quaternion r)
        {
            SwordPose(action, t, out Vector3 sp, out Quaternion sr);
            float g = LeftGripBlend(action, t);
            p = Vector3.Lerp(LeftRest, sp + sr * LeftGripOffset, g);
            r = Quaternion.Slerp(Quaternion.Euler(LeftRestEuler), sr, g);
        }

        [MenuItem("MetinAR/FPP Art/Check Hands + Sword")]
        public static void CheckMenu()
        {
            int errors = Run(out string report);
            Debug.Log(report);
            EditorUtility.DisplayDialog("METINAR-FPP-ART-001", errors == 0 ? "Check passed." : errors + " error(s); see Console.", "OK");
        }

        public static void RunBatch()
        {
            int code = 1;
            try
            {
                int errors = Run(out string report);
                Debug.Log(report);
                code = errors == 0 ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError("[METINAR-FPP-ART-001] Check crashed: " + e);
            }

            EditorApplication.Exit(code);
        }

        private sealed class Ctx
        {
            public readonly StringBuilder Log = new StringBuilder();
            public int Errors;
            public void Ok(string m) => Log.AppendLine("OK    " + m);
            public void Info(string m) => Log.AppendLine("INFO  " + m);
            public void Warn(string m) => Log.AppendLine("WARN  " + m);
            public void Fail(string m) { Errors++; Log.AppendLine("ERROR " + m); }
            public void Check(bool ok, string m) { if (ok) Ok(m); else Fail(m); }
        }

        public static int Run(out string report)
        {
            Directory.CreateDirectory(OutDir);
            var c = new Ctx();
            c.Info("Unity " + Application.unityVersion + " | " + DateTime.UtcNow.ToString("u"));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var armsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FppArtAssetBuilder.ArmsPrefabPath);
            var swordPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FppArtAssetBuilder.SwordPrefabPath);
            c.Check(armsPrefab != null, "PF_Player_FppArms loads");
            c.Check(swordPrefab != null, "PF_Weapon_FppSword loads");
            if (armsPrefab == null || swordPrefab == null)
            {
                report = Finish(c);
                return c.Errors;
            }

            CheckStatic(c, armsPrefab, swordPrefab);

            var root = (GameObject)PrefabUtility.InstantiatePrefab(armsPrefab);
            try
            {
                Transform model = root.transform.Find("Model");
                Transform sock = FppArtAssetBuilder.FindDeep(root.transform, FppArtAssetBuilder.SwordSocketBone);
                Transform sockL = FppArtAssetBuilder.FindDeep(root.transform, FppArtAssetBuilder.LeftGripSocketBone);
                SkinnedMeshRenderer smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
                CheckClips(c, model.gameObject, sock, sockL, smr);
                Screenshots(c, root, model.gameObject, smr);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            report = Finish(c);
            return c.Errors;
        }

        private static string Finish(Ctx c)
        {
            c.Log.Insert(0, "[METINAR-FPP-ART-001] FPP hands + sword asset check: errors=" + c.Errors + "\n");
            File.WriteAllText(ReportPath, c.Log.ToString());
            return c.Log.ToString();
        }

        private static void CheckStatic(Ctx c, GameObject arms, GameObject sword)
        {
            foreach (GameObject go in new[] { arms, sword })
            {
                int missing = go.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
                c.Check(missing == 0, go.name + ": missing scripts = " + missing);
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                {
                    bool matsOk = r.sharedMaterials.Length > 0 && r.sharedMaterials.All(m => m != null && m.shader != null && m.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal));
                    c.Check(matsOk, go.name + "/" + r.name + ": " + r.sharedMaterials.Length + " material(s), all URP (" + string.Join(", ", r.sharedMaterials.Select(m => m == null ? "NULL" : m.name + ":" + m.shader.name)) + ")");
                    c.Check(r.shadowCastingMode == ShadowCastingMode.Off, go.name + "/" + r.name + ": shadows off (camera-attached FPP)");
                    foreach (Material m in r.sharedMaterials.Where(m => m != null))
                    {
                        foreach (string prop in new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap" })
                        {
                            c.Check(m.GetTexture(prop) != null, m.name + "." + prop + " assigned (" + (m.GetTexture(prop) != null ? m.GetTexture(prop).name + " " + m.GetTexture(prop).width + "x" + m.GetTexture(prop).height : "null") + ")");
                        }
                    }

                    Mesh mesh = null;
                    if (r is SkinnedMeshRenderer s) mesh = s.sharedMesh;
                    else
                    {
                        MeshFilter mf = r.GetComponent<MeshFilter>();
                        if (mf != null) mesh = mf.sharedMesh;
                    }

                    c.Check(mesh != null, go.name + "/" + r.name + ": mesh assigned");
                    if (mesh != null)
                    {
                        c.Info(go.name + "/" + r.name + ": " + (mesh.triangles.Length / 3) + " tris, " + mesh.vertexCount + " verts, " + mesh.subMeshCount + " submesh(es), uv0=" + (mesh.uv.Length == mesh.vertexCount) + ", bones=" + ((r as SkinnedMeshRenderer)?.bones.Length ?? 0));
                        c.Check(mesh.uv.Length == mesh.vertexCount, go.name + "/" + r.name + ": UV0 present");
                    }
                }
            }

            foreach (string s in FppArtAssetBuilder.SwordSockets)
            {
                Transform t = FppArtAssetBuilder.FindDeep(sword.transform, s);
                c.Check(t != null, "sword socket " + s + (t != null ? " at " + Fmt(sword.transform.InverseTransformPoint(t.position)) : " missing"));
            }

            Expect(c, sword, "Socket_GripR", Vector3.zero);
            Expect(c, sword, "Socket_GripL", ModelLeftGripOffset);
            Expect(c, sword, "Socket_FxBladeBase", ModelBladeBase);
            Expect(c, sword, "Socket_FxBladeTip", ModelBladeTip);
            Expect(c, sword, "Socket_FxAuraBurst", ModelAuraBurst);
            foreach (string s in new[] { "Socket_GripR", "Socket_FxBladeTip" })
            {
                Transform t = FppArtAssetBuilder.FindDeep(sword.transform, s);
                if (t != null)
                {
                    Quaternion local = Quaternion.Inverse(sword.transform.rotation) * t.rotation;
                    float a = Quaternion.Angle(Quaternion.identity, local);
                    c.Check(a < 0.5f, "sword socket " + s + " frame = RightGrip frame (rotation off by " + a.ToString("0.00") + " deg, local euler " + Fmt(local.eulerAngles) + ")");
                }
            }

            Transform swordInArms = FppArtAssetBuilder.FindDeep(arms.transform, "PF_Weapon_FppSword");
            c.Check(swordInArms != null && swordInArms.parent != null && swordInArms.parent.name == FppArtAssetBuilder.SwordSocketBone && swordInArms.localPosition == Vector3.zero && swordInArms.localRotation == Quaternion.identity,
                "sword nested under " + FppArtAssetBuilder.SwordSocketBone + " with identity local position/rotation");
            c.Check(swordInArms != null && (swordInArms.localScale - Vector3.one * SwordScale).magnitude < 1e-5f && Mathf.Approximately(FppArtAssetBuilder.SwordScale, SwordScale),
                "sword nested at uniform scale " + SwordScale + " around the grip (contract " + FppArtAssetBuilder.ContractId + "; local scale " + (swordInArms != null ? Fmt(swordInArms.localScale) : "n/a") + ")");
            if (swordInArms != null)
            {
                // sockets of the nested sword in the RightGrip (B_Socket_Sword) frame = contract points (model x scale)
                Transform grip = swordInArms.parent;
                foreach (var (socket, expected) in new[] { ("Socket_GripL", LeftGripOffset), ("Socket_FxBladeBase", ModelBladeBase * SwordScale), ("Socket_FxBladeTip", BladeTip), ("Socket_FxAuraBurst", ModelAuraBurst * SwordScale) })
                {
                    Transform t = FppArtAssetBuilder.FindDeep(swordInArms, socket);
                    if (t == null) continue;
                    float d = (grip.InverseTransformPoint(t.position) - expected).magnitude;
                    c.Check(d < 0.001f, "PF_Player_FppArms " + socket + " in RightGrip frame within 1 mm of contract " + Fmt(expected) + " (off " + (d * 1000f).ToString("0.00") + " mm)");
                }
            }
            Animator anim = arms.GetComponentInChildren<Animator>(true);
            c.Check(anim != null && anim.runtimeAnimatorController != null && !anim.applyRootMotion, "Animator with AC_FppArms, no root motion");
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                c.Info("controller clips: " + string.Join(", ", anim.runtimeAnimatorController.animationClips.Select(a => a.name + " " + a.length.ToString("0.000") + "s loop=" + a.isLooping)));
            }

            foreach (string path in new[] { FppArtAssetBuilder.ArmsModelPath }.Concat(FppArtAssetBuilder.States.Select(FppArtAssetBuilder.ClipPath)))
            {
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                c.Check(mi != null && mi.preserveHierarchy && mi.animationType == ModelImporterAnimationType.Generic, Path.GetFileName(path) + ": Generic + Preserve Hierarchy");
            }
        }

        private static void Expect(Ctx c, GameObject sword, string socket, Vector3 expected)
        {
            Transform t = FppArtAssetBuilder.FindDeep(sword.transform, socket);
            if (t == null) return;
            float d = (sword.transform.InverseTransformPoint(t.position) - expected).magnitude;
            c.Check(d < 0.001f, "sword socket " + socket + " within 1 mm of contract " + Fmt(expected) + " (off " + (d * 1000f).ToString("0.00") + " mm)");
        }

        private static void CheckClips(Ctx c, GameObject model, Transform sock, Transform sockL, SkinnedMeshRenderer smr)
        {
            c.Check(sock != null && sockL != null, "socket bones present");
            if (sock == null || sockL == null) return;
            var expected = new Dictionary<string, float> { { "Idle", IdleSeconds }, { "Attack", AttackSeconds }, { "AuraActivate", AuraCastSeconds } };
            Mesh baked = new Mesh();
            int[] handBoneOf = HandSides(smr);
            Camera cam = MakeCamera("VisCam", PortraitAspect, PortraitVerticalFov);
            try
            {
                foreach (string state in FppArtAssetBuilder.States)
                {
                    AnimationClip clip = FppArtAssetBuilder.LoadClip(state);
                    c.Check(clip != null, state + ": clip loads");
                    if (clip == null) continue;
                    c.Check(Mathf.Abs(clip.length - expected[state]) < 0.006f, state + ": length " + clip.length.ToString("0.000") + " s (contract " + expected[state] + " s), frameRate " + clip.frameRate + ", loop " + clip.isLooping);
                    c.Check(clip.isLooping == (state == "Idle"), state + ": loop flag " + clip.isLooping);

                    float maxP = 0f, maxR = 0f, maxPL = 0f, maxRL = 0f, idleAmp = 0f, minClear = 9f;
                    Quaternion worstDelta = Quaternion.identity;
                    int penetrations = 0, samples = 0, visGrip = 0, visTip = 0, visLeft = 0;
                    int n = Mathf.RoundToInt(clip.length * 100f);
                    for (int i = 0; i <= n; i++)
                    {
                        float t = Mathf.Min(i / 100f, clip.length);
                        clip.SampleAnimation(model, t);
                        Vector3 p = model.transform.parent.InverseTransformPoint(sock.position);
                        Quaternion r = Quaternion.Inverse(model.transform.parent.rotation) * sock.rotation;
                        Vector3 pl = model.transform.parent.InverseTransformPoint(sockL.position);
                        Quaternion rl = Quaternion.Inverse(model.transform.parent.rotation) * sockL.rotation;
                        if (state == "Idle")
                        {
                            idleAmp = Mathf.Max(idleAmp, (p - RestPosition).magnitude);
                            if (i == 0 || i == n)
                            {
                                maxP = Mathf.Max(maxP, (p - RestPosition).magnitude);
                                if (Quaternion.Angle(r, Quaternion.Euler(RestEuler)) > maxR) worstDelta = Quaternion.Inverse(Quaternion.Euler(RestEuler)) * r;
                                maxR = Mathf.Max(maxR, Quaternion.Angle(r, Quaternion.Euler(RestEuler)));
                                LeftHandPose("Idle", 0f, out Vector3 ep, out Quaternion er);
                                maxPL = Mathf.Max(maxPL, (pl - ep).magnitude);
                                maxRL = Mathf.Max(maxRL, Quaternion.Angle(rl, er));
                            }
                        }
                        else
                        {
                            SwordPose(state, t, out Vector3 ep, out Quaternion er);
                            maxP = Mathf.Max(maxP, (p - ep).magnitude);
                            if (Quaternion.Angle(r, er) > maxR) worstDelta = Quaternion.Inverse(er) * r;
                            maxR = Mathf.Max(maxR, Quaternion.Angle(r, er));
                            LeftHandPose(state, t, out Vector3 lp, out Quaternion lr);
                            maxPL = Mathf.Max(maxPL, (pl - lp).magnitude);
                            maxRL = Mathf.Max(maxRL, Quaternion.Angle(rl, lr));
                        }

                        // visibility (camera at the prefab root = AR camera)
                        if (InView(cam, p)) visGrip++;
                        if (InView(cam, p + r * BladeTip)) visTip++;
                        if (InView(cam, pl)) visLeft++;

                        // hand/sword intersection on the deformed mesh (every 2nd sample)
                        if (i % 2 == 0 && smr != null)
                        {
                            smr.BakeMesh(baked, true);
                            Vector3[] v = baked.vertices;
                            // -> sword-model units (proxies below are the unscaled model; the sword is drawn at SwordScale)
                            Matrix4x4 toSword = Matrix4x4.Scale(Vector3.one / SwordScale) * sock.worldToLocalMatrix * Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                            float g = LeftGripBlend(state, t);
                            for (int k = 0; k < v.Length; k++)
                            {
                                if (handBoneOf[k] == 0) continue;
                                Vector3 q = toSword.MultiplyPoint3x4(v[k]);
                                float rad = new Vector2(q.x, q.z).magnitude;
                                bool inside = (q.y >= -0.172f && q.y <= 0.104f && rad < 0.0165f)
                                              || (Mathf.Abs(q.x) < 0.11f && Mathf.Abs(q.y - 0.116f) < 0.010f && Mathf.Abs(q.z) < 0.012f)
                                              || (q.y >= 0.13f && q.y <= 0.80f && Mathf.Abs(q.x) < 0.026f && Mathf.Abs(q.z) < 0.004f)
                                              || (rad < 0.026f && Mathf.Abs(q.y + 0.195f) < 0.018f);
                                if (inside) penetrations++;
                                bool gripping = handBoneOf[k] == 1 || g >= 0.5f;
                                if (gripping && q.y >= -0.172f && q.y <= 0.104f && rad < 0.06f) minClear = Mathf.Min(minClear, (rad - 0.0165f) * SwordScale);
                            }

                            samples++;
                        }
                    }

                    string what = state == "Idle" ? "start/end vs rest pose" : "vs contract " + FppArtAssetBuilder.ContractId + " pose (" + (n + 1) + " samples)";
                    c.Check(maxP <= MaxPositionError && maxR <= MaxRotationErrorDeg, state + ": B_Socket_Sword " + what + ": max " + (maxP * 1000f).ToString("0.00") + " mm / " + maxR.ToString("0.00") + " deg (worst local delta euler " + Fmt(worstDelta.eulerAngles) + ")");
                    c.Check(maxPL <= MaxPositionError && maxRL <= MaxRotationErrorDeg, state + ": B_Socket_GripL " + what + ": max " + (maxPL * 1000f).ToString("0.00") + " mm / " + maxRL.ToString("0.00") + " deg");
                    if (state == "Idle") c.Check(idleAmp <= 0.006f, "Idle: sway amplitude " + (idleAmp * 1000f).ToString("0.0") + " mm (<= 6 mm)");
                    c.Check(penetrations == 0, state + ": hand vertices inside sword proxies = " + penetrations + " over " + samples + " sampled frames (min grip clearance " + (minClear * 1000f).ToString("0.0") + " mm)");
                    string vis = state + ": visible in estimated A31 portrait view (" + PortraitVerticalFov + " deg v): grip " + visGrip + "/" + (n + 1) + ", blade tip " + visTip + "/" + (n + 1) + ", left hand " + visLeft + "/" + (n + 1);
                    if (visGrip == 0 && visTip == 0) c.Warn(vis + " (framing is owned by the #31 contract; see doc §6)");
                    else c.Info(vis);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
                UnityEngine.Object.DestroyImmediate(baked);
            }
        }

        /// <summary>Per vertex: 1 = right hand/fingers, -1 = left hand/fingers, 0 = arm (dominant bone).</summary>
        private static int[] HandSides(SkinnedMeshRenderer smr)
        {
            Mesh m = smr.sharedMesh;
            BoneWeight[] w = m.boneWeights;
            var side = new int[m.vertexCount];
            string[] hand = { "B_Hand_", "B_Index", "B_Middle", "B_Ring", "B_Pinky", "B_Thumb" };
            for (int i = 0; i < w.Length; i++)
            {
                float r = 0f, l = 0f;
                void Add(int bi, float wt)
                {
                    if (wt <= 0f) return;
                    string n = smr.bones[bi].name;
                    if (!hand.Any(h => n.StartsWith(h, StringComparison.Ordinal))) return;
                    if (n.EndsWith("_R", StringComparison.Ordinal)) r += wt; else l += wt;
                }

                Add(w[i].boneIndex0, w[i].weight0); Add(w[i].boneIndex1, w[i].weight1); Add(w[i].boneIndex2, w[i].weight2); Add(w[i].boneIndex3, w[i].weight3);
                side[i] = r >= 0.5f ? 1 : l >= 0.5f ? -1 : 0;
            }

            return side;
        }

        private static Camera MakeCamera(string name, float aspect, float vfov)
        {
            var go = new GameObject(name);
            Camera cam = go.AddComponent<Camera>();
            cam.fieldOfView = vfov;
            cam.aspect = aspect;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.44f, 0.47f);
            return cam;
        }

        private static bool InView(Camera cam, Vector3 p)
        {
            Vector3 v = cam.WorldToViewportPoint(p);
            return v.z > cam.nearClipPlane && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
        }

        private static void Screenshots(Ctx c, GameObject root, GameObject model, SkinnedMeshRenderer smr)
        {
            var light = new GameObject("Key").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.transform.rotation = Quaternion.Euler(-10f, 150f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.46f, 0.5f);

            // BakeMesh proxy: batch-mode renders do not re-skin after SampleAnimation (METINAR-013 finding).
            var proxy = new GameObject("SkinProxy");
            var pf = proxy.AddComponent<MeshFilter>();
            var pr = proxy.AddComponent<MeshRenderer>();
            pr.sharedMaterials = smr.sharedMaterials;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            var mesh = new Mesh();
            pf.sharedMesh = mesh;
            smr.enabled = false;

            Camera portrait = MakeCamera("FPP_Portrait", PortraitAspect, PortraitVerticalFov);
            Camera landscape = MakeCamera("FPP_Landscape", 1200f / 540f, 34.6f);
            Camera side = MakeCamera("Side", 4f / 3f, 38f);
            side.transform.position = new Vector3(0.95f, 0.20f, -0.30f);
            side.transform.LookAt(new Vector3(0.02f, -0.15f, 0.40f));
            Camera grip = MakeCamera("Grip", 4f / 3f, 30f);
            grip.transform.position = new Vector3(0.40f, -0.03f, 0.16f);
            grip.transform.LookAt(new Vector3(0.10f, -0.17f, 0.50f));
            var shots = new (string state, float t)[] { ("Idle", 0f), ("Attack", 0.18f), ("Attack", 0.30f), ("Attack", 0.42f), ("Attack", 0.60f), ("AuraActivate", 0.39f), ("AuraActivate", 0.65f), ("AuraActivate", 1.00f) };
            int written = 0;
            try
            {
                foreach (var (state, t) in shots)
                {
                    FppArtAssetBuilder.LoadClip(state).SampleAnimation(model, t);
                    smr.BakeMesh(mesh, true);
                    proxy.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                    string tag = state + "_" + Mathf.RoundToInt(t * 100f).ToString("000");
                    written += Shot(portrait, 540, 1200, "unity_fpp_portrait_" + tag) ? 1 : 0;
                    written += Shot(landscape, 1200, 540, "unity_fpp_landscape_" + tag) ? 1 : 0;
                    written += Shot(side, 960, 720, "unity_side_" + tag) ? 1 : 0;
                    if (state != "Attack") written += Shot(grip, 960, 720, "unity_grip_" + tag) ? 1 : 0;
                }
            }
            finally
            {
                smr.enabled = true;
                foreach (Component o in new Component[] { portrait, landscape, side, grip, light, fill })
                {
                    UnityEngine.Object.DestroyImmediate(o.gameObject);
                }

                UnityEngine.Object.DestroyImmediate(proxy);
                UnityEngine.Object.DestroyImmediate(mesh);
            }

            c.Check(written == shots.Length * 3 + shots.Count(s => s.state != "Attack"), "screenshots written: " + written + " into " + OutDir);
        }

        private static bool Shot(Camera cam, int w, int h, string name)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.aspect = (float)w / h;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(OutDir + "/" + name + ".png", tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                return true;
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static string Fmt(Vector3 v) => "(" + v.x.ToString("0.000") + ", " + v.y.ToString("0.000") + ", " + v.z.ToString("0.000") + ")";
    }
}
