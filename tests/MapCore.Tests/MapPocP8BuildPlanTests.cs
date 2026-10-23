using MetinAR.Editor;
using Xunit;

namespace MapCore.Tests;

public class MapPocP8BuildPlanTests
{
    private sealed class FakeSettings : IBuildSettingsAccess
    {
        public BuildSettingsSnapshot Current;
        public int Persisted;
        public string FailOnApplyField;
        public bool FailFirstRestoreStep;

        public BuildSettingsSnapshot Capture() => Current.Clone();

        public void Apply(BuildSettingsSnapshot v)
        {
            // Partial apply: id first, then optionally fail before the rest (simulates a throwing setter).
            Current.ApplicationIdentifier = v.ApplicationIdentifier;
            if (FailOnApplyField == "name") throw new InvalidOperationException("setter failed");
            Current.ProductName = v.ProductName;
            Current.ScriptingBackend = v.ScriptingBackend;
            Current.TargetArchitectures = v.TargetArchitectures;
            Current.UseCustomKeystore = v.UseCustomKeystore;
            Current.BuildAppBundle = v.BuildAppBundle;
            Current.AndroidBuildSystem = v.AndroidBuildSystem;
            Current.AndroidBuildType = v.AndroidBuildType;
        }

        public IEnumerable<Action> RestoreSteps(BuildSettingsSnapshot o)
        {
            bool first = true;
            yield return () =>
            {
                if (first && FailFirstRestoreStep) { first = false; throw new InvalidOperationException("restore setter failed"); }
                Current.ApplicationIdentifier = o.ApplicationIdentifier;
            };
            yield return () => Current.ProductName = o.ProductName;
            yield return () => Current.ScriptingBackend = o.ScriptingBackend;
            yield return () => Current.TargetArchitectures = o.TargetArchitectures;
            yield return () => Current.UseCustomKeystore = o.UseCustomKeystore;
            yield return () => Current.BuildAppBundle = o.BuildAppBundle;
            yield return () => Current.AndroidBuildSystem = o.AndroidBuildSystem;
            yield return () => Current.AndroidBuildType = o.AndroidBuildType;
        }

        public void Persist() => Persisted++;
    }

    private static BuildSettingsSnapshot Product() => new BuildSettingsSnapshot
    {
        ApplicationIdentifier = "com.metinar.game",
        ProductName = "MetinAR",
        ScriptingBackend = 0,
        TargetArchitectures = 1,
        UseCustomKeystore = true,
        BuildAppBundle = true,
        AndroidBuildSystem = 1,
        AndroidBuildType = 2
    };

    private static BuildSettingsSnapshot Variant(BuildSettingsSnapshot o) => MapPocP8BuildPlan.VariantFrom(o, 1, 2, 1, 0);

    [Fact]
    public void Variant_UsesSeparateIdAndTestLabel()
    {
        BuildSettingsSnapshot v = Variant(Product());
        Assert.Equal("com.metinar.game.mappoc", v.ApplicationIdentifier);
        Assert.Equal("MetinAR Mapa TEST", v.ProductName);
        Assert.NotEqual(MapPocP8BuildPlan.ProductApplicationIdentifier, v.ApplicationIdentifier);
        Assert.True(MapPocP8BuildPlan.IsValidAndroidApplicationId(v.ApplicationIdentifier));
        Assert.False(v.UseCustomKeystore);
        Assert.False(v.BuildAppBundle);
    }

    [Fact]
    public void Body_SeesVariant_AndSettingsAreRestoredAfterSuccess()
    {
        var s = new FakeSettings { Current = Product() };
        string seenId = null, seenName = null;
        bool ok = MapPocP8BuildPlan.RunWithVariant(s, Variant, () =>
        {
            seenId = s.Current.ApplicationIdentifier;
            seenName = s.Current.ProductName;
            return true;
        }, out BuildSettingsSnapshot captured);

        Assert.True(ok);
        Assert.Equal("com.metinar.game.mappoc", seenId);
        Assert.Equal("MetinAR Mapa TEST", seenName);
        Assert.True(s.Current.SameAs(Product()));
        Assert.True(captured.SameAs(Product()));
        Assert.Equal(1, s.Persisted);
    }

    [Fact]
    public void FailedBuild_StillRestores()
    {
        var s = new FakeSettings { Current = Product() };
        bool ok = MapPocP8BuildPlan.RunWithVariant(s, Variant, () => false, out _);
        Assert.False(ok);
        Assert.True(s.Current.SameAs(Product()));
    }

    [Fact]
    public void ExceptionInBuild_StillRestores()
    {
        var s = new FakeSettings { Current = Product() };
        Assert.Throws<InvalidOperationException>(() =>
            MapPocP8BuildPlan.RunWithVariant<bool>(s, Variant, () => throw new InvalidOperationException("build crashed"), out _));
        Assert.True(s.Current.SameAs(Product()));
        Assert.Equal(1, s.Persisted);
    }

    [Fact]
    public void ExceptionDuringPartialApply_StillRestores()
    {
        var s = new FakeSettings { Current = Product(), FailOnApplyField = "name" };
        bool bodyRan = false;
        Assert.Throws<InvalidOperationException>(() =>
            MapPocP8BuildPlan.RunWithVariant(s, Variant, () => bodyRan = true, out _));
        Assert.False(bodyRan);
        Assert.True(s.Current.SameAs(Product()));
    }

    [Fact]
    public void OneFailingRestoreStep_DoesNotStopTheOthers_AndIsReported()
    {
        var s = new FakeSettings { Current = Product(), FailFirstRestoreStep = true };
        var ex = Assert.Throws<InvalidOperationException>(() => MapPocP8BuildPlan.RunWithVariant(s, Variant, () => true, out _));
        Assert.Contains("restore", ex.Message);
        Assert.Equal("MetinAR", s.Current.ProductName);       // later steps still ran
        Assert.True(s.Current.UseCustomKeystore);
        Assert.True(s.Current.BuildAppBundle);
        Assert.Equal(1, s.Persisted);
    }

    [Theory]
    [InlineData("com.metinar.game")]          // product id: would overwrite FPP
    [InlineData("com.metinar.game.")]
    [InlineData("1com.metinar")]
    [InlineData("mappoc")]
    [InlineData("com.metinar.map-poc")]
    [InlineData("")]
    public void RefusesProductOrInvalidId_WithoutTouchingSettings(string id)
    {
        var s = new FakeSettings { Current = Product() };
        bool bodyRan = false;
        Assert.Throws<InvalidOperationException>(() => MapPocP8BuildPlan.RunWithVariant(s, o =>
        {
            BuildSettingsSnapshot v = Variant(o);
            v.ApplicationIdentifier = id;
            return v;
        }, () => bodyRan = true, out _));
        Assert.False(bodyRan);
        Assert.True(s.Current.SameAs(Product()));
        Assert.Equal(0, s.Persisted);
    }

    [Fact]
    public void RefusesVariantEqualToCurrentId_EvenIfNotProductId()
    {
        BuildSettingsSnapshot current = Product();
        current.ApplicationIdentifier = "com.metinar.game.mappoc";
        var s = new FakeSettings { Current = current };
        Assert.Throws<InvalidOperationException>(() => MapPocP8BuildPlan.RunWithVariant(s, Variant, () => true, out _));
    }

    [Fact]
    public void ApkPath_IsSeparateFromOldPocApk()
    {
        Assert.Equal("Builds/Android/MetinAR-mappoc-p8-dev.apk", MapPocP8BuildPlan.ApkRelativePath);
        Assert.NotEqual("Builds/Android/MetinAR-osm-poc-dev.apk", MapPocP8BuildPlan.ApkRelativePath);
    }
}
