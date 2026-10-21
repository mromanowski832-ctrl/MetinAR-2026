using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MetinAR.AssetPipeline.Tests
{
    /// <summary>
    /// METINAR-FPP-ART-001 (#34): the baked clips must come from contract V3 "v3-a31-retest"
    /// (#31 issuecomment-6314043034 = PR #32 8d0acee FppViewFraming = FppPoseContract.RetestV3).
    /// Engine-free source checks: the Blender contract (fpp_contract.py), the Unity check reference values
    /// (FppArtAssetCheck.cs) and the builder constants (FppArtAssetBuilder.cs) agree with V3, and the builder
    /// keeps AC_FppArms.controller in place (no delete/recreate, so no fileID churn on rebuild).
    /// </summary>
    public class FppArtContractTests
    {
        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "art-source", "FppHandsSword", "fpp_contract.py"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string Src(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));
        private static string Py() => Src("art-source", "FppHandsSword", "fpp_contract.py");
        private static string Check() => Src("client", "Assets", "MetinAR", "Editor", "FppArt", "FppArtAssetCheck.cs");
        private static string Builder() => Src("client", "Assets", "MetinAR", "Editor", "FppArt", "FppArtAssetBuilder.cs");

        private static double[] Nums(string s) => Regex.Matches(s, @"-?\d+(?:\.\d+)?")
            .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();

        /// <summary>Numbers of a python assignment line `NAME = ...` (first match at line start).</summary>
        private static double[] PyValue(string name)
        {
            Match m = Regex.Match(Py(), "^" + name + @"\s*=\s*(.+)$", RegexOptions.Multiline);
            Assert.True(m.Success, name + " missing in fpp_contract.py");
            string expr = m.Groups[1].Value.Split('#')[0];
            return Nums(expr);
        }

        /// <summary>Numbers of a C# `NAME = new Vector3(...)` initialiser in FppArtAssetCheck.cs.</summary>
        private static double[] CsVector(string src, string name)
        {
            Match m = Regex.Match(src, @"\b" + name + @"\s*=\s*new Vector3\(([^)]*)\)");
            Assert.True(m.Success, name + " missing");
            return Nums(m.Groups[1].Value.Replace("f", ""));
        }

        public static readonly object[][] V3 =
        {
            new object[] { "REST_POSITION", "RestPosition", new[] { 0.09, -0.17, 0.46 } },
            new object[] { "REST_EULER", "RestEuler", new[] { 15.0, -30.0, -22.0 } },
            new object[] { "LEFT_REST", "LeftRest", new[] { -0.08, -0.23, 0.42 } },
            new object[] { "LEFT_REST_EULER", "LeftRestEuler", new[] { 0.0, 0.0, 12.0 } },
        };

        [Theory]
        [MemberData(nameof(V3))]
        public void ContractV3_PythonAndUnityCheckAgree(string py, string cs, double[] expected)
        {
            Assert.Equal(expected, PyValue(py));
            Assert.Equal(expected, CsVector(Check(), cs));
        }

        [Fact]
        public void ContractV3_AuraHold()
        {
            Assert.Equal(new[] { 0.10, -0.14, 0.50, 10.0, -30.0, -18.0 }, PyValue("AURA_POSE"));
            Assert.Equal(new[] { 0.10, -0.14, 0.50 }, CsVector(Check(), "AuraPos"));
            Assert.Equal(new[] { 10.0, -30.0, -18.0 }, CsVector(Check(), "AuraEuler"));
        }

        [Fact]
        public void ContractV3_WindupAndEndUnchanged()
        {
            Assert.Equal(new[] { 0.25, -0.08, 0.48, 0.0, -10.0, -65.0 }, PyValue("ATTACK_WINDUP"));
            Assert.Equal(new[] { -0.15, -0.22, 0.50, 70.0, 30.0, 60.0 }, PyValue("ATTACK_END"));
            Assert.Equal(new[] { 0.72 }, PyValue("ATTACK_SECONDS"));
            Assert.Equal(new[] { 0.18 }, PyValue("HIT_START_SECONDS"));
            Assert.Equal(new[] { 0.42 }, PyValue("HIT_END_SECONDS"));
            Assert.Equal(new[] { 1.15 }, PyValue("AURA_CAST_SECONDS"));
        }

        [Fact]
        public void ContractV3_SwordScaleAndScaledPoints()
        {
            Assert.Equal(new[] { 0.75 }, PyValue("SWORD_SCALE"));
            Assert.Matches(@"public const float SwordScale = 0\.75f;", Check());
            Assert.Matches(@"public const float SwordScale = 0\.75f;", Builder());
            // unscaled model points (sword FBX sockets) and their scaled contract counterparts
            Assert.Equal(new[] { 0.0, 0.13, 0.0 }, PyValue("MODEL_BLADE_BASE"));
            Assert.Equal(new[] { 0.0, 0.94, 0.0 }, PyValue("MODEL_BLADE_TIP"));
            Assert.Equal(new[] { 0.0, -0.12, 0.0 }, PyValue("MODEL_LEFT_GRIP_OFFSET"));
            foreach (string n in new[] { "BLADE_BASE", "BLADE_TIP", "LEFT_GRIP_OFFSET", "AURA_BURST_CENTRE", "GUARD_CENTRE" })
            {
                Assert.Matches(new Regex("^" + n + @" = MODEL_" + n + @" \* SWORD_SCALE", RegexOptions.Multiline), Py());
            }

            Assert.Contains("LeftGripOffset = ModelLeftGripOffset * SwordScale", Check());
            Assert.Contains("BladeTip = ModelBladeTip * SwordScale", Check());
            Assert.Contains("sword.transform.localScale = Vector3.one * SwordScale", Builder());
        }

        [Fact]
        public void ContractV3_IdMatchesIntegrationConfigId()
        {
            Assert.Matches("^CONTRACT_ID = \"v3-a31-retest\"", Py().Split('\n').First(l => l.StartsWith("CONTRACT_ID", StringComparison.Ordinal)));
            Assert.Contains("public const string ContractId = \"v3-a31-retest\";", Builder());
        }

        [Fact]
        public void ObsoleteContractValues_NotUsed()
        {
            // V1 rest (0.18,-0.19,0.46) only as a labelled reference; the earlier proposal (0.06,-0.15,0.44) nowhere
            Assert.DoesNotMatch(@"^REST_POSITION = Vector\(\(0\.18", Py());
            Assert.DoesNotContain("Vector((0.06, -0.15, 0.44", Py());
            Assert.DoesNotContain("0.06f, -0.15f, 0.44f", Check());
            Assert.DoesNotContain("new Vector3(0.18f, -0.19f, 0.46f)", Check());
        }

        [Fact]
        public void Builder_KeepsControllerAsset_NoDeleteRecreate()
        {
            string b = Builder();
            int start = b.IndexOf("private static AnimatorController BuildController()", StringComparison.Ordinal);
            Assert.True(start > 0);
            int end = b.IndexOf("private static GameObject BuildSwordPrefab", start, StringComparison.Ordinal);
            string body = b.Substring(start, end - start);
            Assert.DoesNotContain("DeleteAsset", body);
            Assert.Contains("LoadAssetAtPath<AnimatorController>(ControllerPath)", body);
            Assert.Matches(@"if \(controller == null\) controller = AnimatorController\.CreateAnimatorControllerAtPath", body);
            Assert.DoesNotContain("DeleteAsset", b);
        }
    }
}
