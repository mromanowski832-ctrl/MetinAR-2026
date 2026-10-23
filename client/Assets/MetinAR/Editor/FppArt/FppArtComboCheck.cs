using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MetinAR.ArEncounter;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Ctx = MetinAR.Editor.FppArt.FppArtAssetCheck.Ctx;

namespace MetinAR.Editor.FppArt
{
    /// <summary>
    /// #91 part of FppArtAssetCheck (read-only, called from FppArtAssetCheck.Run):
    /// - weapon prefabs PF_Weapon_FppSword / FppSword02 / FppGreatsword: sockets, URP materials, emission, budgets;
    /// - PF_Player_FppArms: two material slots (glove M_FppArms_GloveWarrior01 = slot 0, sleeve M_FppArms = slot 1),
    ///   B_Socket_ShieldL, FppArmsAnimationEvents receiver, budgets;
    /// - EVERY clip (V3 + combo/block/aura): length, loop flag, OnFppAnimEvent events and B_Socket_Sword /
    ///   B_Socket_GripL / B_Socket_ShieldL at every frame vs art-source/FppHandsSword/clip_reference.json (2 mm / 1 deg);
    /// - hand vertices inside the per-weapon proxies (fpp_gear.PROXIES port), left hand >= 50 mm from the drawn hilt
    ///   on every frame it is not gripping;
    /// - buckler states: #63 buckler analytic solid (fpp_buckler.part_at port, margin 0) on B_Socket_ShieldL vs arms,
    ///   hands and the 1H sword; central screen box (20 %) blocked by the buckler in the A31 portrait view.
    /// </summary>
    internal static class FppArtComboCheck
    {
        public const int ArmsTriangleBudget = 10000, ArmsBoneBudget = 40, WeaponTriangleBudget = 3000;
        public const float EventTimeTolerance = 0.011f; // one 100 Hz frame + float rounding
        public const float CentreBox = 0.20f;
        public const float MaxCentreBlocked = 0.10f; // buckler may pass through the central box briefly (Blender: 8 % max)

        // ---- fpp_gear.PROXIES (sword-model-local, unscaled): hilt (y0, y1, r), guard (x, y0, y1, z), blade (y0, y1, x, z), pommel (y0, y1, r)
        private sealed class Proxy
        {
            public float Hb, Ht, Hr, Gx, Gy0, Gy1, Gz, By0, By1, Bx, Bz, Py0, Py1, Pr;
            public Vector3 SegBottom, SegTop; // drawn-hilt segment for the left-hand distance

            public bool Hit(Vector3 q)
            {
                float r = new Vector2(q.x, q.z).magnitude;
                if (q.y >= Hb && q.y <= Ht && r < Hr) return true;
                if (Mathf.Abs(q.x) < Gx && q.y >= Gy0 && q.y <= Gy1 && Mathf.Abs(q.z) < Gz && (Mathf.Abs(q.x) < 0.04f || q.y < Gy0 + 0.035f + 0.5f * Mathf.Abs(q.x))) return true;
                if (q.y >= By0 && q.y <= By1 && Mathf.Abs(q.x) < Bx && Mathf.Abs(q.z) < Bz) return true;
                return q.y >= Py0 && q.y <= Py1 && r < Pr;
            }
        }

        private static readonly Dictionary<string, Proxy> Proxies = new Dictionary<string, Proxy>
        {
            { "SM_Weapon_FppSword", new Proxy { Hb = -0.182f, Ht = 0.104f, Hr = 0.0165f, Gx = 0.125f, Gy0 = 0.096f, Gy1 = 0.160f, Gz = 0.024f, By0 = 0.13f, By1 = 0.94f, Bx = 0.0335f, Bz = 0.0066f, Py0 = -0.236f, Py1 = -0.172f, Pr = 0.026f, SegBottom = FppArtAssetCheck.ModelHiltBottom, SegTop = FppArtAssetCheck.ModelHiltTop } },
            { "SM_Weapon_FppSword02", new Proxy { Hb = -0.182f, Ht = 0.104f, Hr = 0.0165f, Gx = 0.110f, Gy0 = 0.100f, Gy1 = 0.150f, Gz = 0.020f, By0 = 0.13f, By1 = 0.94f, Bx = 0.0390f, Bz = 0.0064f, Py0 = -0.236f, Py1 = -0.172f, Pr = 0.030f, SegBottom = FppArtAssetCheck.ModelHiltBottom, SegTop = FppArtAssetCheck.ModelHiltTop } },
            { "SM_Weapon_FppGreatsword", new Proxy { Hb = -0.246f, Ht = 0.100f, Hr = 0.0165f, Gx = 0.172f, Gy0 = 0.100f, Gy1 = 0.196f, Gz = 0.028f, By0 = 0.14f, By1 = 1.30f, Bx = 0.046f, Bz = 0.0085f, Py0 = -0.320f, Py1 = -0.236f, Pr = 0.030f, SegBottom = new Vector3(0f, -0.246f, 0f), SegTop = new Vector3(0f, 0.100f, 0f) } },
        };

        // ---- fpp_buckler.py port: #63 SM_Weapon_StarterBuckler (PR #63 @ 1dd895a) analytic solid in the B_Socket_ShieldL frame
        private const float RShield = 0.170f, REdge = 0.172f, RimOut = 0.009f, BackD = 0.035f, Thick = 0.012f, Dome = 0.018f, BossR = 0.064f, BossH = 0.048f, HandleR = 0.0135f;
        private static float DFront(float r) => BackD + Thick + Dome * (1f - Sq(Mathf.Min(r, RShield) / RShield));
        private static float DBack(float r) => BackD + Dome * (1f - Sq(Mathf.Min(r, RShield) / RShield));
        private static float Sq(float x) => x * x;

        private static readonly Vector3[] HandlePath =
        {
            new Vector3(0f, -0.082f, DBack(0.08f) + 0.004f), new Vector3(0f, -0.068f, 0.018f), new Vector3(0f, -0.042f, 0f), new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0.042f, 0f), new Vector3(0f, 0.068f, 0.018f), new Vector3(0f, 0.082f, DBack(0.08f) + 0.004f),
        };

        private enum Part { None, Planks, Rim, Boss, Handle }

        private static Part BucklerPart(Vector3 lu)
        {
            float r = new Vector2(lu.x, lu.y).magnitude, d = lu.z;
            if (r <= REdge + RimOut && d >= DBack(r) - 0.004f && d <= DFront(r) + 0.004f) return r <= RShield ? Part.Planks : Part.Rim;
            if (r <= BossR + 0.012f)
            {
                float top = DFront(BossR) + BossH * Mathf.Sqrt(Mathf.Max(0f, 1f - Sq(r / (BossR + 0.012f))));
                if (d >= DFront(r) && d <= top) return Part.Boss;
            }

            if (Mathf.Abs(lu.y) <= 0.09f && d <= DBack(0.08f) + 0.01f)
            {
                for (int i = 0; i < HandlePath.Length - 1; i++)
                {
                    if (SegDist(lu, HandlePath[i], HandlePath[i + 1]) < HandleR) return Part.Handle;
                }
            }

            return Part.None;
        }

        private static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            return (a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) - p).magnitude;
        }

        /// <summary>fpp_combo.shield_hang: 1 = buckler parked on the outer left forearm (AuraActivate1H two-hand grip).</summary>
        public static float ShieldHang(string state, float t)
        {
            if (state != "AuraActivate1H") return 0f;
            return t < 0.90f ? Mathf.Clamp01((t - 0.08f) / 0.20f) : 1f - Mathf.Clamp01((t - 0.90f) / 0.20f);
        }

        /// <summary>fpp_build.left_gripping: left hand on the hilt (Aura grip blend >= 0.5, every 2H frame).</summary>
        public static bool LeftGripping(FppArtAssetBuilder.ClipRef r, float t)
        {
            if (r.state == "AuraActivate" || r.state == "AuraActivate1H") return FppArtAssetCheck.LeftGripBlend("AuraActivate", t) > 1e-6f;
            return r.weapon == "SM_Weapon_FppGreatsword";
        }

        public static void CheckStatic(Ctx c, GameObject arms)
        {
            foreach (FppArtAssetBuilder.WeaponDef w in FppArtAssetBuilder.Weapons)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(w.PrefabPath);
                c.Check(prefab != null, Path.GetFileName(w.PrefabPath) + " loads");
                if (prefab == null) continue;
                int missing = prefab.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
                c.Check(missing == 0, prefab.name + ": missing scripts = " + missing);
                int tris = 0, mats = 0;
                foreach (MeshRenderer r in prefab.GetComponentsInChildren<MeshRenderer>(true))
                {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                    mats += r.sharedMaterials.Length;
                    bool ok = r.sharedMaterials.All(m => m != null && m.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal)
                                                         && m.GetTexture("_BaseMap") != null && m.GetTexture("_BumpMap") != null && m.GetTexture("_MetallicGlossMap") != null && m.GetTexture("_EmissionMap") != null);
                    c.Check(ok && r.shadowCastingMode == ShadowCastingMode.Off, prefab.name + "/" + r.name + ": URP Lit BC/N/MS/E (" + string.Join(", ", r.sharedMaterials.Select(m => m == null ? "NULL" : m.name)) + "), shadows off");
                }

                c.Check(tris > 0 && tris <= WeaponTriangleBudget && mats == 1, prefab.name + ": " + tris + " tris (<= " + WeaponTriangleBudget + "), " + mats + " material (1)");
                foreach (string s in FppArtAssetBuilder.SwordSockets)
                {
                    c.Check(FppArtAssetBuilder.FindDeep(prefab.transform, s) != null, prefab.name + ": socket " + s);
                }

                Transform gr = FppArtAssetBuilder.FindDeep(prefab.transform, "Socket_GripR");
                if (gr != null) c.Check(prefab.transform.InverseTransformPoint(gr.position).magnitude < 0.001f && Quaternion.Angle(Quaternion.identity, Quaternion.Inverse(prefab.transform.rotation) * gr.rotation) < 0.5f, prefab.name + ": Socket_GripR = pivot (swap on B_Socket_Sword at scale " + FppArtAssetCheck.SwordScale + ")");
            }

            SkinnedMeshRenderer smr = arms.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null && smr.sharedMesh != null)
            {
                Material[] m = smr.sharedMaterials;
                c.Check(m.Length == 2 && smr.sharedMesh.subMeshCount == 2 && m[0] != null && m[1] != null
                        && m[0].name == Path.GetFileNameWithoutExtension(FppArtAssetBuilder.GloveMaterialPath) && m[1].name == Path.GetFileNameWithoutExtension(FppArtAssetBuilder.ArmsMaterialPath),
                    "PF_Player_FppArms: slot 0 = glove " + (m.Length > 0 && m[0] != null ? m[0].name : "?") + ", slot 1 = sleeve " + (m.Length > 1 && m[1] != null ? m[1].name : "?") + " (glove swappable)");
                int tris = smr.sharedMesh.triangles.Length / 3;
                c.Check(tris <= ArmsTriangleBudget && smr.bones.Length <= ArmsBoneBudget, "PF_Player_FppArms: " + tris + " tris (<= " + ArmsTriangleBudget + "), " + smr.bones.Length + " bones (<= " + ArmsBoneBudget + ")");
                for (int s = 0; s < smr.sharedMesh.subMeshCount; s++)
                {
                    c.Info("arms submesh " + s + ": " + smr.sharedMesh.GetSubMesh(s).indexCount / 3 + " tris");
                }
            }

            c.Check(FppArtAssetBuilder.FindDeep(arms.transform, FppArtAssetBuilder.ShieldSocketBone) != null, "PF_Player_FppArms: " + FppArtAssetBuilder.ShieldSocketBone + " (buckler attach, #63 pivot)");
            c.Check(arms.GetComponentInChildren<FppArmsAnimationEvents>(true) != null, "PF_Player_FppArms: FppArmsAnimationEvents receiver on the animated model");
            Animator anim = arms.GetComponentInChildren<Animator>(true);
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                var names = anim.runtimeAnimatorController.animationClips.Select(a => a.name).ToList();
                c.Check(FppArtAssetBuilder.States.All(s => names.Any(n => n.EndsWith("_" + s, StringComparison.Ordinal) || n == s)), "AC_FppArms holds all " + FppArtAssetBuilder.States.Length + " clips");
            }
        }

        public static void CheckClips(Ctx c, GameObject root, GameObject model, SkinnedMeshRenderer smr)
        {
            FppArtAssetBuilder.ClipReference refs;
            try
            {
                refs = FppArtAssetBuilder.LoadClipReference();
            }
            catch (Exception e)
            {
                c.Fail("clip_reference.json: " + e.Message);
                return;
            }

            c.Check(FppArtAssetBuilder.States.All(s => FppArtAssetBuilder.FindClip(refs, s) != null) && refs.clips.Length == FppArtAssetBuilder.States.Length,
                "clip_reference.json: " + refs.clips.Length + " clips, fps " + refs.fps + ", buckler " + refs.buckler_asset);
            Transform sock = FppArtAssetBuilder.FindDeep(model.transform, FppArtAssetBuilder.SwordSocketBone);
            Transform sockL = FppArtAssetBuilder.FindDeep(model.transform, FppArtAssetBuilder.LeftGripSocketBone);
            Transform sockS = FppArtAssetBuilder.FindDeep(model.transform, FppArtAssetBuilder.ShieldSocketBone);
            Transform nested = FppArtAssetBuilder.FindDeep(model.transform, "PF_Weapon_" + FppArtAssetBuilder.SwordId);
            if (sock == null || sockL == null || sockS == null || smr == null)
            {
                c.Fail("socket bones / skinned mesh missing");
                return;
            }

            Transform space = model.transform.parent;
            int[] handSide = HandSides(smr);
            int[] cls = DominantClasses(smr);
            Vector3[] swordVerts = nested == null ? new Vector3[0] : nested.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null)
                .SelectMany(f => { Matrix4x4 m = nested.worldToLocalMatrix * f.transform.localToWorldMatrix; return f.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)); }).ToArray();
            Vector3[] swordSamples = SwordSamples(Proxies["SM_Weapon_FppSword"]);
            var baked = new Mesh();
            try
            {
                foreach (string state in FppArtAssetBuilder.States)
                {
                    FppArtAssetBuilder.ClipRef r = FppArtAssetBuilder.FindClip(refs, state);
                    AnimationClip clip = FppArtAssetBuilder.LoadClip(state);
                    if (r == null || clip == null)
                    {
                        c.Fail(state + ": clip or reference missing");
                        continue;
                    }

                    c.Check(Mathf.Abs(clip.length - r.seconds) < 0.006f && clip.isLooping == FppArtAssetBuilder.IsLoop(state) && r.loop == FppArtAssetBuilder.IsLoop(state),
                        "[#91] " + state + ": " + clip.length.ToString("0.000") + " s (ref " + r.seconds + "), loop " + clip.isLooping + " (expected " + FppArtAssetBuilder.IsLoop(state) + ")");
                    AnimationEvent[] ev = clip.events;
                    bool evOk = ev.Length == r.events.Length;
                    for (int i = 0; evOk && i < ev.Length; i++)
                    {
                        evOk = ev[i].functionName == FppArtAssetBuilder.EventFunction && ev[i].stringParameter == r.events[i].id && Mathf.Abs(ev[i].time - r.events[i].time) <= EventTimeTolerance;
                    }

                    c.Check(evOk, "[#91] " + state + ": events " + string.Join(" ", ev.Select(e => e.stringParameter + "@" + e.time.ToString("0.00"))) + " (ref " + string.Join(" ", r.events.Select(e => e.id + "@" + e.time.ToString("0.00"))) + ")");

                    Proxy proxy = Proxies.TryGetValue(r.weapon, out Proxy px) ? px : null;
                    if (proxy == null)
                    {
                        c.Fail(state + ": unknown weapon " + r.weapon);
                        continue;
                    }

                    float maxP = 0f, maxR = 0f, freeMin = 9f, freeAt = -1f, centreMax = 0f, centreAt = 0f;
                    int pen = 0, penFrames = 0;
                    var bk = new int[5]; // right arm/hand, left arm, left hand planks/rim/boss, left hand handle (parked), sword
                    foreach (FppArtAssetBuilder.SampleRef s in r.samples)
                    {
                        float t = Mathf.Min(s.t, clip.length);
                        clip.SampleAnimation(model, t);
                        foreach (var (tr, v) in new[] { (sock, s.sword), (sockL, s.gripL), (sockS, s.shieldL) })
                        {
                            Vector3 p = space.InverseTransformPoint(tr.position);
                            Quaternion q = Quaternion.Inverse(space.rotation) * tr.rotation;
                            maxP = Mathf.Max(maxP, (p - new Vector3(v[0], v[1], v[2])).magnitude);
                            maxR = Mathf.Max(maxR, Quaternion.Angle(q, new Quaternion(v[3], v[4], v[5], v[6])));
                        }

                        // drawn sword frame (B_Socket_Sword x SwordScale, the 2H greatsword swaps onto the same socket)
                        Matrix4x4 swordM = Matrix4x4.TRS(sock.position, sock.rotation, Vector3.one * FppArtAssetCheck.SwordScale);
                        if (!LeftGripping(r, t))
                        {
                            Vector3 a = swordM.MultiplyPoint3x4(proxy.SegBottom), b = swordM.MultiplyPoint3x4(proxy.SegTop);
                            float d = SegDist(sockL.position, a, b);
                            if (d < freeMin) { freeMin = d; freeAt = t; }
                        }

                        bool everyFrame = r.buckler;
                        int frame = Mathf.RoundToInt(s.t * refs.fps);
                        if (!everyFrame && frame % 2 != 0) continue;
                        smr.BakeMesh(baked, true);
                        Vector3[] vs = baked.vertices;
                        Matrix4x4 bakedToWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                        Matrix4x4 toSword = swordM.inverse * bakedToWorld;
                        int before = pen;
                        for (int k = 0; k < vs.Length; k++)
                        {
                            if (handSide[k] != 0 && proxy.Hit(toSword.MultiplyPoint3x4(vs[k]))) pen++;
                        }

                        if (pen > before) penFrames++;
                        if (!r.buckler) continue;
                        Matrix4x4 toShield = Matrix4x4.TRS(sockS.position, sockS.rotation, Vector3.one).inverse;
                        Matrix4x4 meshToShield = toShield * bakedToWorld;
                        float hang = ShieldHang(state, t);
                        for (int k = 0; k < vs.Length; k++)
                        {
                            if (cls[k] == 0) continue;
                            Part part = BucklerPart(meshToShield.MultiplyPoint3x4(vs[k]));
                            if (part == Part.None) continue;
                            if (cls[k] == 1) bk[0]++;
                            else if (cls[k] == 2) bk[1]++;
                            else if (part != Part.Handle) bk[2]++;
                            else if (hang > 0.5f) bk[3]++;
                        }

                        if (nested != null)
                        {
                            Matrix4x4 sw = toShield * nested.localToWorldMatrix;
                            bk[4] += swordVerts.Count(v => BucklerPart(sw.MultiplyPoint3x4(v)) != Part.None) + swordSamples.Count(v => BucklerPart(sw.MultiplyPoint3x4(v)) != Part.None);
                        }

                        float cb = CentreBlocked(space, toShield);
                        if (cb > centreMax) { centreMax = cb; centreAt = t; }
                    }

                    c.Check(maxP <= FppArtAssetCheck.MaxPositionError && maxR <= FppArtAssetCheck.MaxRotationErrorDeg,
                        "[#91] " + state + ": B_Socket_Sword/GripL/ShieldL vs clip_reference (" + r.samples.Length + " frames): max " + (maxP * 1000f).ToString("0.00") + " mm / " + maxR.ToString("0.00") + " deg");
                    c.Check(pen == 0, "[#91] " + state + ": hand vertices inside " + r.weapon + " proxies = " + pen + " (" + penFrames + " frames)");
                    if (freeAt < 0f) c.Info("[#91] " + state + ": left hand grips the hilt on every frame (no free-hand distance)");
                    else c.Check(freeMin >= FppArtAssetCheck.MinHiltLeftClearance, "[#91] " + state + ": free left hand min distance to the drawn hilt " + (freeMin * 1000f).ToString("0.0") + " mm at " + freeAt.ToString("0.00") + " s (>= 50 mm)");
                    if (r.buckler)
                    {
                        c.Check(bk.Sum() == 0, "[#91] " + state + ": buckler (#63 solid on " + FppArtAssetBuilder.ShieldSocketBone + ") hits: right arm/hand " + bk[0] + ", left arm " + bk[1] + ", left hand in planks/rim/boss " + bk[2] + ", left hand in handle (parked) " + bk[3] + ", sword " + bk[4]);
                        c.Check(centreMax <= MaxCentreBlocked, "[#91] " + state + ": central " + (CentreBox * 100f).ToString("0") + " % screen box blocked by the buckler max " + (centreMax * 100f).ToString("0") + " % at " + centreAt.ToString("0.00") + " s (<= " + (MaxCentreBlocked * 100f).ToString("0") + " %)");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baked);
            }
        }

        /// <summary>fpp_buckler.centre_blocked: rays from the AR camera (prefab root) through the central box, A31 portrait.</summary>
        private static float CentreBlocked(Transform space, Matrix4x4 toShield, int n = 5)
        {
            float ty = Mathf.Tan(FppArtAssetCheck.PortraitVerticalFov * Mathf.Deg2Rad / 2f), tx = ty * FppArtAssetCheck.PortraitAspect;
            int hit = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    var d = new Vector3(CentreBox * (-1f + 2f * i / (n - 1)) * tx, CentreBox * (-1f + 2f * j / (n - 1)) * ty, 1f);
                    for (int k = 0; k < 180; k++)
                    {
                        if (BucklerPart(toShield.MultiplyPoint3x4(space.TransformPoint(d * (0.10f + 0.005f * k)))) != Part.None) { hit++; break; }
                    }
                }
            }

            return hit / (float)(n * n);
        }

        private static Vector3[] SwordSamples(Proxy p)
        {
            var pts = new List<Vector3>();
            int n = (int)((p.By1 - p.By0) / 0.008f);
            for (int i = 0; i <= n; i++)
            {
                float y = p.By0 + (p.By1 - p.By0) * i / n;
                foreach (float x in new[] { -p.Bx, -p.Bx * 0.5f, 0f, p.Bx * 0.5f, p.Bx })
                {
                    pts.Add(new Vector3(x, y, -p.Bz));
                    pts.Add(new Vector3(x, y, p.Bz));
                }
            }

            for (int i = 0; i < 13; i++)
            {
                float y = p.Hb + (p.Ht - p.Hb) * i / 12f;
                for (int k = 0; k < 8; k++) pts.Add(new Vector3(p.Hr * Mathf.Cos(k * Mathf.PI / 4f), y, p.Hr * Mathf.Sin(k * Mathf.PI / 4f)));
            }

            for (int i = 0; i < 15; i++)
            {
                float x = -p.Gx + 2f * p.Gx * i / 14f;
                foreach (float y in new[] { p.Gy0, Mathf.Abs(x) < 0.04f ? p.Gy1 : p.Gy0 + 0.02f })
                {
                    pts.Add(new Vector3(x, y, -p.Gz));
                    pts.Add(new Vector3(x, y, p.Gz));
                }
            }

            foreach (float y in new[] { p.Py0, p.Py1 })
            {
                for (int k = 0; k < 8; k++) pts.Add(new Vector3(p.Pr * Mathf.Cos(k * Mathf.PI / 4f), y, p.Pr * Mathf.Sin(k * Mathf.PI / 4f)));
            }

            return pts.ToArray();
        }

        /// <summary>fpp_anim.hand_vertex_sets: 1 = right hand/fingers, -1 = left (summed weight >= 0.5), 0 = arm.</summary>
        private static int[] HandSides(SkinnedMeshRenderer smr)
        {
            BoneWeight[] w = smr.sharedMesh.boneWeights;
            string[] hand = { "B_Hand_", "B_Index", "B_Middle", "B_Ring", "B_Pinky", "B_Thumb" };
            var side = new int[smr.sharedMesh.vertexCount];
            for (int i = 0; i < w.Length; i++)
            {
                float r = 0f, l = 0f;
                foreach (var (bi, wt) in Influences(w[i]))
                {
                    string n = smr.bones[bi].name;
                    if (wt <= 0f || !hand.Any(h => n.StartsWith(h, StringComparison.Ordinal))) continue;
                    if (n.EndsWith("_R", StringComparison.Ordinal)) r += wt; else l += wt;
                }

                side[i] = r >= 0.5f ? 1 : l >= 0.5f ? -1 : 0;
            }

            return side;
        }

        /// <summary>fpp_buckler.vertex_classes by dominant bone: 1 = right side, 2 = left forearm/upper arm, 3 = left hand, 0 = none.</summary>
        private static int[] DominantClasses(SkinnedMeshRenderer smr)
        {
            BoneWeight[] w = smr.sharedMesh.boneWeights;
            var cls = new int[smr.sharedMesh.vertexCount];
            for (int i = 0; i < w.Length; i++)
            {
                var best = Influences(w[i]).OrderByDescending(x => x.wt).First();
                if (best.wt <= 0f) continue;
                string n = smr.bones[best.bi].name;
                if (n.EndsWith("_R", StringComparison.Ordinal)) cls[i] = 1;
                else if (n.EndsWith("_L", StringComparison.Ordinal)) cls[i] = n.Contains("Forearm") || n.Contains("UpperArm") ? 2 : 3;
            }

            return cls;
        }

        private static (int bi, float wt)[] Influences(BoneWeight b) =>
            new[] { (b.boneIndex0, b.weight0), (b.boneIndex1, b.weight1), (b.boneIndex2, b.weight2), (b.boneIndex3, b.weight3) };

        /// <summary>Unity previews of the #91 clips (portrait A31 view): 2H shots swap the greatsword onto B_Socket_Sword in the
        /// temporary instance only. The #63 buckler is not on this branch, so Unity shots show the shield hand without it
        /// (Blender previews in docs/METINAR-FPP-ART-001/ show the buckler).</summary>
        public static void Screenshots(Ctx c, GameObject model, SkinnedMeshRenderer smr)
        {
            var shots = new (string state, float t)[] { ("Attack1H_01", 0.30f), ("Attack1H_03", 0.35f), ("ShieldBlockHold", 0.5f), ("AuraActivate1H", 0.65f), ("Idle2H", 0f), ("Attack2H_01", 0.47f), ("Attack2H_03", 0.55f), ("AuraActivate2H", 0.65f) };
            Transform sock = FppArtAssetBuilder.FindDeep(model.transform, FppArtAssetBuilder.SwordSocketBone);
            Transform nested = FppArtAssetBuilder.FindDeep(model.transform, "PF_Weapon_" + FppArtAssetBuilder.SwordId);
            var gsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FppArtAssetBuilder.Weapons[2].PrefabPath);
            if (sock == null || nested == null || gsPrefab == null) { c.Fail("[#91] screenshots: socket/sword/greatsword missing"); return; }
            var gs = (GameObject)PrefabUtility.InstantiatePrefab(gsPrefab);
            gs.transform.SetParent(sock, false);
            gs.transform.localScale = Vector3.one * FppArtAssetCheck.SwordScale;
            var light = new GameObject("Key").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.46f, 0.5f);
            var proxy = new GameObject("SkinProxy");
            var pf = proxy.AddComponent<MeshFilter>();
            var pr = proxy.AddComponent<MeshRenderer>();
            pr.sharedMaterials = smr.sharedMaterials;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            var mesh = new Mesh();
            pf.sharedMesh = mesh;
            smr.enabled = false;
            Camera portrait = FppArtAssetCheck.MakeCamera("FPP_Portrait91", FppArtAssetCheck.PortraitAspect, FppArtAssetCheck.PortraitVerticalFov);
            int written = 0;
            try
            {
                foreach (var (state, t) in shots)
                {
                    bool twoHand = state.Contains("2H");
                    gs.SetActive(twoHand);
                    nested.gameObject.SetActive(!twoHand);
                    FppArtAssetBuilder.LoadClip(state).SampleAnimation(model, t);
                    smr.BakeMesh(mesh, true);
                    mesh.RecalculateBounds();
                    proxy.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                    FppArtAssetCheck.Shot(portrait, 540, 1200, null);
                    written += FppArtAssetCheck.Shot(portrait, 540, 1200, "unity91_fpp_portrait_" + state + "_" + Mathf.RoundToInt(t * 100f).ToString("000")) ? 1 : 0;
                }
            }
            finally
            {
                smr.enabled = true;
                nested.gameObject.SetActive(true);
                UnityEngine.Object.DestroyImmediate(gs);
                UnityEngine.Object.DestroyImmediate(portrait.gameObject);
                UnityEngine.Object.DestroyImmediate(light.gameObject);
                UnityEngine.Object.DestroyImmediate(proxy);
                UnityEngine.Object.DestroyImmediate(mesh);
            }

            c.Check(written == shots.Length, "[#91] screenshots written: " + written + " into " + FppArtAssetCheck.OutDir);
        }
    }
}
