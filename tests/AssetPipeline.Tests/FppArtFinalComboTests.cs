using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MetinAR.Editor.AssetPipeline;
using Xunit;

namespace MetinAR.AssetPipeline.Tests
{
    /// <summary>
    /// #91 (METINAR-FPP-ART-001 final): engine-free checks of the final combo/block/aura set.
    /// clip_reference.json (written by fpp_build.py final91) is the single source the Unity builder (events, loop flags)
    /// and FppArtComboCheck (sockets, events) read, so it must agree with the builder state lists and stay internally sane.
    /// The V3 contract values themselves are covered by FppArtContractTests.
    /// </summary>
    public class FppArtFinalComboTests
    {
        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "art-source", "FppHandsSword", "fpp_contract.py"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string P(params string[] parts) => Path.Combine(new[] { Root() }.Concat(parts).ToArray());
        private static string Builder() => File.ReadAllText(P("client", "Assets", "MetinAR", "Editor", "FppArt", "FppArtAssetBuilder.cs"));
        private static string ComboCheck() => File.ReadAllText(P("client", "Assets", "MetinAR", "Editor", "FppArt", "FppArtComboCheck.cs"));

        private static JsonElement Ref()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(P("art-source", "FppHandsSword", "clip_reference.json")));
            return doc.RootElement.Clone();
        }

        private static string[] CsStringArray(string src, string name)
        {
            Match m = Regex.Match(src, @"\b" + name + @"\s*=\s*\{([^}]*)\}");
            Assert.True(m.Success, name + " missing");
            return Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\"").Select(x => x.Groups[1].Value).ToArray();
        }

        private static readonly string[] V3 = { "Idle", "Attack", "AuraActivate" };
        private static readonly string[] Combo =
        {
            "Attack1H_01", "Attack1H_02", "Attack1H_03", "Attack2H_01", "Attack2H_02", "Attack2H_03",
            "AuraActivate1H", "AuraActivate2H", "Idle2H", "ShieldIdle", "ShieldBlock", "ShieldBlockHold", "ShieldBlockEnd",
        };
        private static readonly string[] Loops = { "Idle", "Idle2H", "ShieldIdle", "ShieldBlockHold" };

        [Fact]
        public void Builder_StateListsMatchTheFinalSet()
        {
            string b = Builder();
            Assert.Equal(V3, CsStringArray(b, "V3States"));
            Assert.Equal(Combo.OrderBy(s => s), CsStringArray(b, "ComboStates").OrderBy(s => s));
            Assert.Equal(Loops.OrderBy(s => s), CsStringArray(b, "LoopStates").OrderBy(s => s));
            Assert.Contains("ShieldSocketBone = \"B_Socket_ShieldL\"", b);
            Assert.Contains("EventFunction = \"OnFppAnimEvent\"", b);
            Assert.Contains("ClipReferencePath = \"../art-source/FppHandsSword/clip_reference.json\"", b);
            Assert.Contains("AddComponent<FppArmsAnimationEvents>", b);
            foreach (string w in new[] { "FppSword", "FppSword02", "FppGreatsword" }) Assert.Contains("\"" + w + "\"", b);
            Assert.DoesNotContain("DeleteAsset", b);
        }

        [Fact]
        public void EventReceiver_HasTheMethodTheClipsCall()
        {
            string src = File.ReadAllText(P("client", "Assets", "MetinAR", "Scripts", "ArEncounter", "FppArmsAnimationEvents.cs"));
            Assert.Contains("public void OnFppAnimEvent(string id)", src);
            Assert.True(File.Exists(P("client", "Assets", "MetinAR", "Scripts", "ArEncounter", "FppArmsAnimationEvents.cs.meta")));
        }

        [Fact]
        public void ComboCheck_LimitsMatchAcceptance()
        {
            string c = ComboCheck();
            Assert.Contains("MinHiltLeftClearance", c);
            Assert.Contains("ArmsTriangleBudget = 10000, ArmsBoneBudget = 40, WeaponTriangleBudget = 3000", c);
            Assert.Contains("MaxCentreBlocked = 0.10f", c);
        }

        [Fact]
        public void ClipReference_HasAllSixteenClipsWithV3Contract()
        {
            JsonElement r = Ref();
            Assert.Equal("v3-a31-retest", r.GetProperty("contract_id").GetString());
            Assert.Equal(100, r.GetProperty("fps").GetInt32());
            string[] states = r.GetProperty("clips").EnumerateArray().Select(c => c.GetProperty("state").GetString()!).ToArray();
            Assert.Equal(V3.Concat(Combo).OrderBy(s => s), states.OrderBy(s => s));
            Assert.Equal(states.Length, states.Distinct().Count());
        }

        public static IEnumerable<object[]> AllStates() => V3.Concat(Combo).Select(s => new object[] { s });

        private static JsonElement Clip(string state) =>
            Ref().GetProperty("clips").EnumerateArray().Single(c => c.GetProperty("state").GetString() == state);

        [Theory]
        [MemberData(nameof(AllStates))]
        public void ClipReference_ClipIsConsistent(string state)
        {
            JsonElement c = Clip(state);
            int fps = 100;
            int frames = c.GetProperty("frames").GetInt32();
            double seconds = c.GetProperty("seconds").GetDouble();
            Assert.Equal((frames - 1) / (double)fps, seconds, 6);
            Assert.Equal("A_FppArms_" + state + ".fbx", c.GetProperty("file").GetString());
            Assert.Equal(Loops.Contains(state), c.GetProperty("loop").GetBoolean());
            Assert.True(File.Exists(P("client", "Assets", "MetinAR", "Characters", "Player", "FppArms", "Animations", "A_FppArms_" + state + ".fbx")), state + " FBX in the repo");

            bool twoHand = state.Contains("2H");
            Assert.Equal(twoHand ? "SM_Weapon_FppGreatsword" : "SM_Weapon_FppSword", c.GetProperty("weapon").GetString());
            bool buckler = state.StartsWith("Attack1H") || state.StartsWith("Shield") || state == "AuraActivate1H";
            Assert.Equal(buckler, c.GetProperty("buckler").GetBoolean());          // K6: 1H always carries the buckler, 2H never
            Assert.Equal(twoHand || state.StartsWith("AuraActivate"), c.GetProperty("leftGrips").GetBoolean());

            var ev = c.GetProperty("events").EnumerateArray().Select(e => (id: e.GetProperty("id").GetString()!, t: e.GetProperty("time").GetDouble())).ToArray();
            Assert.NotEmpty(ev);
            for (int i = 0; i < ev.Length; i++)
            {
                Assert.InRange(ev[i].t, 0.0, seconds);
                if (i > 0) Assert.True(ev[i].t >= ev[i - 1].t, state + ": events sorted");
                Assert.Matches("^[A-Z][A-Za-z]+$", ev[i].id);
            }

            string[] ids = ev.Select(e => e.id).ToArray();
            if (state.StartsWith("Attack"))
            {
                double hs = ev.Single(e => e.id == "HitStart").t, he = ev.Single(e => e.id == "HitEnd").t, rec = ev.Single(e => e.id == "Recovered").t;
                Assert.True(hs < he && he < rec, state + ": HitStart < HitEnd < Recovered");
                bool opener = state.EndsWith("_01") || state.EndsWith("_02");
                Assert.Equal(opener, ids.Contains("ComboWindowOpen"));
                Assert.Equal(opener, ids.Contains("ComboWindowClose"));
            }
            if (state.StartsWith("AuraActivate"))
                Assert.Equal(new[] { "AuraRaise", "AuraReach", "AuraGrip", "AuraStabilize", "AuraChargeStart", "AuraSpread", "AuraFlash", "AuraSustain", "AuraReturn", "Recovered" }, ids); // #93: 9 steps
            if (state == "ShieldBlock") Assert.Equal(new[] { "BlockRaise", "BlockActive", "BlockHold" }, ids);
            if (state == "ShieldBlockEnd") Assert.Equal(new[] { "BlockRelease", "Recovered" }, ids);

            var samples = c.GetProperty("samples").EnumerateArray().ToArray();
            Assert.Equal(frames, samples.Length);
            foreach (string key in new[] { "sword", "gripL", "shieldL" })
            {
                double[] v = samples[0].GetProperty(key).EnumerateArray().Select(x => x.GetDouble()).ToArray();
                Assert.Equal(7, v.Length);
                Assert.Equal(1.0, Math.Sqrt(v.Skip(3).Sum(x => x * x)), 3);
            }
        }

        [Fact]
        public void ClipReference_V3ClipsStartAtTheContractRestPose()
        {
            // Idle t=0 sword socket = REST_POSITION (0.09,-0.17,0.46); left grip = LEFT_REST (-0.08,-0.23,0.42)
            JsonElement s = Clip("Idle").GetProperty("samples")[0];
            double[] sw = s.GetProperty("sword").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            double[] gl = s.GetProperty("gripL").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            Assert.Equal(new[] { 0.09, -0.17, 0.46 }, sw.Take(3).Select(x => Math.Round(x, 4)));
            Assert.Equal(new[] { -0.08, -0.23, 0.42 }, gl.Take(3).Select(x => Math.Round(x, 4)));
        }

        public static IEnumerable<object[]> AssetFolders() => new[]
        {
            new object[] { "Characters/Player/FppArms", "FppArms" },
            new object[] { "Weapons/FppSword", "FppSword" },
            new object[] { "Weapons/FppSword02", "FppSword02" },
            new object[] { "Weapons/FppGreatsword", "FppGreatsword" },
        };

        [Theory]
        [MemberData(nameof(AssetFolders))]
        public void AssetFolder_PathsAndProvenancePassThePipelineRules(string folder, string id)
        {
            string abs = P(new[] { "client", "Assets", "MetinAR" }.Concat(folder.Split('/')).ToArray());
            string manifestPath = Path.Combine(abs, "ASSET_SOURCE.txt");
            var m = AssetSourceManifest.Parse(File.ReadAllText(manifestPath));
            Assert.Equal("original", m.Get("origin"));
            Assert.Empty(m.Validate("Assets/MetinAR/" + folder + "/ASSET_SOURCE.txt", id).Where(i => i.Severity == IssueSeverity.Error));
            foreach (string f in Directory.GetFiles(abs, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".meta")))
            {
                string rel = "Assets/MetinAR/" + Path.GetRelativePath(P("client", "Assets", "MetinAR"), f).Replace('\\', '/');
                var errors = AssetPipelineRules.ValidatePath(rel).Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Message).ToArray();
                Assert.True(errors.Length == 0, rel + ": " + string.Join("; ", errors));
            }
        }
    }
}
