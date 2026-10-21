using System.IO.Compression;
using MetinAR.Editor.Build;
using Xunit;

namespace MetinAR.QA.Tests;

/// <summary>
/// #31 regression: the 52a1375 dev APK contained sharedassets1.assets (new build) together with stale
/// sharedassets1.assets.split0..14 from an older build (copied Library / Gradle export). The player loaded the
/// stale chunks and crashed when opening the AR scene. The build script now fails such an APK.
/// </summary>
public class AndroidPlayerDataValidatorTests
{
    private const string D = AndroidPlayerDataValidator.DataPrefix;

    /// <summary>assets/bin/Data top-level layout of MetinAR-FPP001-52a1375-dev.apk (crashing build).</summary>
    private static List<string> CrashingApk52a1375()
    {
        var e = new List<string>
        {
            "AndroidManifest.xml",
            "classes.dex",
            "lib/arm64-v8a/libil2cpp.so",
            D + "boot.config",
            D + "globalgamemanagers",
            D + "globalgamemanagers.assets.split0",
            D + "globalgamemanagers.assets.split1",
            D + "globalgamemanagers.assets.split2",
            D + "globalgamemanagers.assets.split3",
            D + "level0",
            D + "level1",
            D + "sharedassets0.assets.split0",
            D + "sharedassets0.assets.split1",
            D + "sharedassets1.assets",
            D + "unity_app_guid",
            D + "RuntimeInitializeOnLoads.json",
            D + "ScriptingAssemblies.json",
            D + "0000000000000000f000000000000000",
            D + "215687feeab0b7445b4a1cd86c82c2e1",
            D + "Managed/Metadata/global-metadata.dat",
            D + "Resources/unity_builtin_extra",
        };
        for (int i = 0; i <= 14; i++)
        {
            e.Add(D + "sharedassets1.assets.split" + i);
        }

        return e;
    }

    [Fact]
    public void CrashingApkLayout_IsRejected_WithSharedAssets1Duplicate()
    {
        IReadOnlyList<string> problems = AndroidPlayerDataValidator.FindProblems(CrashingApk52a1375());

        Assert.Single(problems);
        Assert.Contains(D + "sharedassets1.assets exists both whole and as 15 .split chunk(s)", problems[0]);
    }

    [Fact]
    public void CleanLayout_WithoutStaleChunks_Passes()
    {
        List<string> entries = CrashingApk52a1375();
        entries.RemoveAll(n => n.StartsWith(D + "sharedassets1.assets.split", StringComparison.Ordinal));

        Assert.Empty(AndroidPlayerDataValidator.FindProblems(entries));
    }

    [Fact]
    public void SplitOnlyFiles_AreValid()
    {
        var entries = new[] { D + "sharedassets0.assets.split0", D + "sharedassets0.assets.split1", D + "level0" };

        Assert.Empty(AndroidPlayerDataValidator.FindProblems(entries));
    }

    [Fact]
    public void MissingChunk_IsReported()
    {
        var entries = new[] { D + "x.assets.split0", D + "x.assets.split2" };

        IReadOnlyList<string> problems = AndroidPlayerDataValidator.FindProblems(entries);

        Assert.Single(problems);
        Assert.Contains("missing .split1", problems[0]);
    }

    [Fact]
    public void ChunksNotStartingAtZero_AreReported()
    {
        IReadOnlyList<string> problems = AndroidPlayerDataValidator.FindProblems(new[] { D + "y.assets.split1" });

        Assert.Single(problems);
        Assert.Contains("missing .split0", problems[0]);
    }

    [Fact]
    public void EntriesOutsideDataRoot_AndSubfolders_AreIgnored()
    {
        var entries = new[]
        {
            "assets/other/a.assets", "assets/other/a.assets.split0",
            D + "Managed/b.assets", D + "Managed/b.assets.split0",
            D + "Data.splitter", D + ".split0",
        };

        Assert.Empty(AndroidPlayerDataValidator.FindProblems(entries));
    }

    [Fact]
    public void BackslashSeparators_AreNormalized()
    {
        var entries = new[] { "assets\\bin\\Data\\s.assets", "assets\\bin\\Data\\s.assets.split0" };

        Assert.Single(AndroidPlayerDataValidator.FindProblems(entries));
    }

    [Fact]
    public void NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AndroidPlayerDataValidator.FindProblems(null));
    }

    /// <summary>
    /// Optional check of a real APK: set METINAR_VALIDATE_APK to its path. Without the variable this test is a no-op
    /// (it is not evidence about any APK).
    /// </summary>
    [Fact]
    public void OptionalRealApk_FromEnvironment_HasConsistentPlayerData()
    {
        string path = Environment.GetEnvironmentVariable("METINAR_VALIDATE_APK");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        using ZipArchive zip = ZipFile.OpenRead(path);
        IReadOnlyList<string> problems = AndroidPlayerDataValidator.FindProblems(zip.Entries.Select(x => x.FullName));

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
