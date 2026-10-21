using System.IO;
using MetinAR.Editor.Build;
using Xunit;

namespace MetinAR.QA.Tests;

/// <summary>#31: integration APK f194aa9 had no ARCore registrations → "Failed to load session subsystem" on A31.</summary>
public class ArCorePlayerDataValidatorTests
{
    private static readonly string[] Entries =
    {
        ArCorePlayerDataValidator.RuntimeInitializeOnLoadsEntry,
        ArCorePlayerDataValidator.ArCoreSubsystemsManifestEntry,
        ArCorePlayerDataValidator.ArCoreNativeLibraryEntry,
        "assets/bin/Data/globalgamemanagers",
    };

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void WorkingFeatApkPasses()
    {
        var problems = ArCorePlayerDataValidator.FindProblems(Entries, Fixture("RuntimeInitializeOnLoads-8d0acee-arcore-ok.json"));
        Assert.Empty(problems);
    }

    [Fact]
    public void BrokenIntegrationApkIsRejectedForTheSessionSubsystem()
    {
        var problems = ArCorePlayerDataValidator.FindProblems(Entries, Fixture("RuntimeInitializeOnLoads-f194aa9-arcore-missing.json"));
        Assert.Equal(ArCorePlayerDataValidator.RequiredRegistrations.Length, problems.Count);
        Assert.Contains(problems, p => p.Contains("ARCoreSessionSubsystem.RegisterDescriptor"));
    }

    [Fact]
    public void AllElevenArCoreRegistrationsAreRequiredAndCounted()
    {
        Assert.Equal(11, ArCorePlayerDataValidator.RequiredRegistrations.Length);
        Assert.Equal(11, ArCorePlayerDataValidator.CountRequiredRegistrations(Fixture("RuntimeInitializeOnLoads-8d0acee-arcore-ok.json")));
        Assert.Equal(0, ArCorePlayerDataValidator.CountRequiredRegistrations(Fixture("RuntimeInitializeOnLoads-f194aa9-arcore-missing.json")));
    }

    [Fact]
    public void OneMissingRegistrationFailsTheApk()
    {
        string ok = Fixture("RuntimeInitializeOnLoads-8d0acee-arcore-ok.json");
        string noOcclusion = ok.Replace("\"className\":\"ARCoreOcclusionSubsystem\"", "\"className\":\"Removed\"");
        Assert.NotEqual(ok, noOcclusion);
        var problems = ArCorePlayerDataValidator.FindProblems(Entries, noOcclusion);
        Assert.Single(problems);
        Assert.Contains("ARCoreOcclusionSubsystem.Register", problems[0]);
        Assert.Equal(10, ArCorePlayerDataValidator.CountRequiredRegistrations(noOcclusion));
    }

    [Fact]
    public void MissingFilesAreReported()
    {
        var problems = ArCorePlayerDataValidator.FindProblems(new[] { "assets/bin/Data/globalgamemanagers" }, null);
        Assert.Equal(3, problems.Count);
    }

    [Theory]
    [InlineData("{\"root\":[{\"assemblyName\":\"Unity.XR.ARCore\",\"nameSpace\":\"UnityEngine.XR.ARCore\",\"className\":\"ARCoreSessionSubsystem\",\"methodName\":\"RegisterDescriptor\",\"loadTypes\":4}]}", true)]
    [InlineData("{ \"root\" : [ { \"assemblyName\" : \"Unity.XR.ARCore\", \"className\" : \"ARCoreSessionSubsystem\", \"methodName\" : \"RegisterDescriptor\" } ] }", true)]
    [InlineData("{\"root\":[{\"assemblyName\":\"Unity.XR.ARKit\",\"className\":\"ARCoreSessionSubsystem\",\"methodName\":\"RegisterDescriptor\"}]}", false)]
    [InlineData("{\"root\":[{\"assemblyName\":\"Unity.XR.ARCore\",\"className\":\"ARCoreSessionSubsystem\",\"methodName\":\"Other\"},{\"assemblyName\":\"Unity.XR.ARCore\",\"className\":\"Other\",\"methodName\":\"RegisterDescriptor\"}]}", false)]
    [InlineData("", false)]
    public void RegistrationMustBeOneRecord(string json, bool expected)
    {
        Assert.Equal(expected, ArCorePlayerDataValidator.HasRegistration(json, "Unity.XR.ARCore", "ARCoreSessionSubsystem.RegisterDescriptor"));
    }
}
