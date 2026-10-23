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
            // ---- #36 MANAGER DECISION 2026-10-21T13:33Z (option 1): left-hand dodge during Attack, >= 50 mm hilt clearance

        [Fact]
        public void LeftAttackDodge_PythonAndUnityCheckAgree()
        {
            Assert.Equal(new[] { -0.08, -0.03, -0.04 }, PyValue("LEFT_ATTACK_DODGE"));
            Assert.Equal(new[] { -0.08, -0.03, -0.04 }, CsVector(Check(), "LeftAttackDodge"));
            Assert.Equal(new[] { 0.18, 0.30, 0.48, 0.70 }, PyValue("LEFT_ATTACK_DODGE_TIMES"));
            Assert.Contains("LeftAttackDodgeTimes = { 0.18f, 0.30f, 0.48f, 0.70f }", Check());
            Assert.Equal(new[] { 0.050 }, PyValue("MIN_HILT_LEFT_CLEARANCE_M"));
            Assert.Contains("MinHiltLeftClearance = 0.050f", Check());
            Assert.Equal(new[] { -0.215, 0.116 }, PyValue("HILT_SEGMENT_MODEL_Y"));
            Assert.Contains("ModelHiltBottom = new Vector3(0f, -0.215f, 0f), ModelHiltTop = new Vector3(0f, 0.116f, 0f)", Check());
            Assert.True(PyValue("LEFT_ATTACK_DODGE_TIMES")[3] < PyValue("ATTACK_SECONDS")[0], "the left hand is back at rest before the Attack clip ends");
        }

        [Fact]
        public void LeftAttackDodge_HiltStaysAtLeast50mmFromLeftSocket_WholeAttack()
        {
            double min = MinHiltLeft(out double at, dodge: true);
            Assert.True(min >= 0.050, "min hilt-to-left-socket distance " + (min * 1000).ToString("0.0", CultureInfo.InvariantCulture) + " mm at " + at.ToString("0.000", CultureInfo.InvariantCulture) + " s");
            Assert.InRange(min, 0.0738, 0.0758); // = fpp_build report attack_hilt_left_socket (74.8 mm at 0.42 s): port agrees
            Assert.InRange(at, 0.41, 0.43);
            // and the decision was needed: without the dodge V3 crosses the hand (measured 5.5-5.7 mm)
            Assert.True(MinHiltLeft(out _, dodge: false) < 0.010);
        }

        // engine-free port of fpp_contract.sword_pose / left_hand_pose (Unity Euler = Qy*Qx*Qz, SmoothStep, Slerp)
        private static double MinHiltLeft(out double at, bool dodge)
        {
            double[] rest = PyValue("REST_POSITION"), restE = PyValue("REST_EULER"), left = PyValue("LEFT_REST");
            double[] wu = PyValue("ATTACK_WINDUP"), en = PyValue("ATTACK_END"), dv = PyValue("LEFT_ATTACK_DODGE"), dt = PyValue("LEFT_ATTACK_DODGE_TIMES");
            double scale = PyValue("SWORD_SCALE")[0], hs = PyValue("HIT_START_SECONDS")[0], he = PyValue("HIT_END_SECONDS")[0], len = PyValue("ATTACK_SECONDS")[0];
            double[] seg = PyValue("HILT_SEGMENT_MODEL_Y");
            Q qr = Q.Euler(restE[0], restE[1], restE[2]), qw = Q.Euler(wu[3], wu[4], wu[5]), qe = Q.Euler(en[3], en[4], en[5]);
            double[] pr = rest, pw = wu.Take(3).ToArray(), pe = en.Take(3).ToArray();
            double min = 9; at = 0;
            for (int f = 0; f <= 720; f++)
            {
                double t = f / 1000.0;
                double[] p; Q r;
                if (t < hs) { double k = Ease(t / hs); p = Lerp(pr, pw, k); r = Q.Slerp(qr, qw, k); }
                else if (t <= he) { double k = Ease((t - hs) / (he - hs)); p = Lerp(pw, pe, k); r = Q.Slerp(qw, qe, k); }
                else { double k = Ease((t - he) / (len - he)); p = Lerp(pe, pr, k); r = Q.Slerp(qe, qr, k); }
                double w = 0;
                if (dodge && t > dt[0] && t < dt[3]) w = t < dt[1] ? Ease((t - dt[0]) / (dt[1] - dt[0])) : t <= dt[2] ? 1 : 1 - Ease((t - dt[2]) / (dt[3] - dt[2]));
                double[] l = { left[0] + dv[0] * w, left[1] + dv[1] * w, left[2] + dv[2] * w };
                double[] a = Add(p, r.Rotate(new[] { 0, seg[0] * scale, 0 })), b = Add(p, r.Rotate(new[] { 0, seg[1] * scale, 0 }));
                double[] ab = Sub(b, a), al = Sub(l, a);
                double u = Math.Max(0, Math.Min(1, Dot(al, ab) / Dot(ab, ab)));
                double[] c = Sub(Add(a, new[] { ab[0] * u, ab[1] * u, ab[2] * u }), l);
                double d = Math.Sqrt(Dot(c, c));
                if (d < min) { min = d; at = t; }
            }

            return min;
        }

        private static double Ease(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }
        private static double[] Lerp(double[] a, double[] b, double t) => new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t };
        private static double[] Add(double[] a, double[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };
        private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

        private readonly struct Q
        {
            private readonly double _w, _x, _y, _z;
            private Q(double w, double x, double y, double z) { _w = w; _x = x; _y = y; _z = z; }
            private static Q Axis(double x, double y, double z, double deg) { double h = deg * Math.PI / 360; double s = Math.Sin(h); return new Q(Math.Cos(h), x * s, y * s, z * s); }
            private static Q Mul(Q a, Q b) => new Q(a._w * b._w - a._x * b._x - a._y * b._y - a._z * b._z,
                a._w * b._x + a._x * b._w + a._y * b._z - a._z * b._y,
                a._w * b._y - a._x * b._z + a._y * b._w + a._z * b._x,
                a._w * b._z + a._x * b._y - a._y * b._x + a._z * b._w);
            public static Q Euler(double x, double y, double z) => Mul(Mul(Axis(0, 1, 0, y), Axis(1, 0, 0, x)), Axis(0, 0, 1, z));
            public static Q Slerp(Q a, Q b, double t)
            {
                double dot = a._w * b._w + a._x * b._x + a._y * b._y + a._z * b._z;
                if (dot < 0) { b = new Q(-b._w, -b._x, -b._y, -b._z); dot = -dot; }
                if (dot > 0.9995) { var q = new Q(a._w + (b._w - a._w) * t, a._x + (b._x - a._x) * t, a._y + (b._y - a._y) * t, a._z + (b._z - a._z) * t); double n = Math.Sqrt(q._w * q._w + q._x * q._x + q._y * q._y + q._z * q._z); return new Q(q._w / n, q._x / n, q._y / n, q._z / n); }
                double th = Math.Acos(dot), s = Math.Sin(th), ka = Math.Sin((1 - t) * th) / s, kb = Math.Sin(t * th) / s;
                return new Q(a._w * ka + b._w * kb, a._x * ka + b._x * kb, a._y * ka + b._y * kb, a._z * ka + b._z * kb);
            }

            public double[] Rotate(double[] v)
            {
                var r = Mul(Mul(this, new Q(0, v[0], v[1], v[2])), new Q(_w, -_x, -_y, -_z));
                return new[] { r._x, r._y, r._z };
            }
        }
    }
}
